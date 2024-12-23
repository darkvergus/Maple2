﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Maple2.Database.Extensions;
using Maple2.Database.Model;
using Maple2.Model.Common;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Microsoft.EntityFrameworkCore;
using Z.EntityFramework.Plus;
using Home = Maple2.Model.Game.Home;
using Item = Maple2.Model.Game.Item;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        public IList<Plot> LoadPlotsForMap(int mapId, long ownerId) {
            IQueryable<UgcMap> query;
            if (ownerId >= 0) {
                query = Context.UgcMap.Include(map => map.Cubes)
                    .Where(map => map.MapId == mapId && map.OwnerId == ownerId);
            } else {
                query = Context.UgcMap.Include(map => map.Cubes)
                    .Where(map => map.MapId == mapId);
            }

            return query.AsEnumerable()
                .ToList() // ToList before Select so 'This MySqlConnection is already in use.' exception doesn't occur.
                .Select(ToPlot)
                .ToList()!;
        }

        public IList<PlotCube> LoadCubesForOwner(long ownerId) {
            List<PlotCube> plotCubes = Context.UgcMap.Where(map => map.OwnerId == ownerId)
                .Join(Context.UgcMapCube, ugcMap => ugcMap.Id, cube => cube.UgcMapId, (ugcMap, cube) => cube)
                .AsEnumerable()
                .Select<UgcMapCube, PlotCube>(cube => cube)
                .ToList();
            foreach (PlotCube cube in plotCubes) {
                if (!cube.ItemType.IsInteractFurnishing) continue;
                game.itemMetadata.TryGet(cube.ItemId, out ItemMetadata? itemMetadata);
                if (itemMetadata?.Install is null) continue;

                game.functionCubeMetadata.TryGet(itemMetadata.Install.InteractId, out FunctionCubeMetadata? functionCubeMetadata);
                if (functionCubeMetadata?.Nurturing is null) continue;

                cube.Interact!.Nurturing = GetNurturing(ownerId, cube.ItemId);
            }

            return plotCubes;
        }

        public PlotInfo? BuyPlot(string characterName, long ownerId, PlotInfo plot, TimeSpan days) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            UgcMap? ugcMap = Context.UgcMap.FirstOrDefault(map => map.Id == plot.Id && !map.Indoor);
            if (ugcMap == null) {
                return null;
            }

            Debug.Assert(ugcMap.MapId == plot.MapId && ugcMap.Number == plot.Number && ugcMap.ApartmentNumber == plot.ApartmentNumber);
            if (ugcMap.OwnerId != 0 || ugcMap.ExpiryTime >= DateTime.Now) {
                return null;
            }

            ugcMap.OwnerId = ownerId;
            ugcMap.ExpiryTime = DateTime.UtcNow + days;
            ugcMap.Name = characterName;
            Context.UgcMap.Update(ugcMap);
            Context.UgcMapCube.Where(cube => cube.UgcMapId == ugcMap.Id).Delete();

            return Context.TrySaveChanges() ? ToPlotInfo(ugcMap) : null;
        }

        public PlotInfo? ExtendPlot(PlotInfo plot, TimeSpan days) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            UgcMap? model = Context.UgcMap.Find(plot.Id);
            if (model == null) {
                return null;
            }

            Debug.Assert(model.MapId == plot.MapId && model.Number == plot.Number && model.ApartmentNumber == plot.ApartmentNumber);
            if (model.ExpiryTime < DateTime.Now) {
                return null;
            }

            model.ExpiryTime += days;
            Context.UgcMap.Update(model);

            return Context.TrySaveChanges() ? ToPlotInfo(model) : null;
        }

        public PlotInfo? ForfeitPlot(long ownerId, PlotInfo plot) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            UgcMap? model = Context.UgcMap.Find(plot.Id);
            if (model == null || model.OwnerId != ownerId) {
                return null;
            }

            Debug.Assert(model.MapId == plot.MapId && model.Number == plot.Number && model.ApartmentNumber == plot.ApartmentNumber);
            if (model.ExpiryTime < DateTime.Now) {
                return null;
            }

            model.OwnerId = 0;
            model.Name = string.Empty;
            model.ExpiryTime = DateTimeOffset.UtcNow;
            Context.UgcMapCube.Where(cube => cube.UgcMapId == model.Id).Delete();
            Context.UgcMap.Update(model);

            return Context.TrySaveChanges() ? ToPlotInfo(model) : null;
        }

        public bool SaveHome(Home home) {
            Model.Home model = home;
            Context.Home.Update(model);
            if (!Context.TrySaveChanges()) {
                return false;
            }

            home.LastModified = model.LastModified.ToEpochSeconds();
            return true;
        }

        public bool SavePlotInfo(params PlotInfo[] plotInfos) {
            foreach (PlotInfo plotInfo in plotInfos) {
                UgcMap? model = Context.UgcMap.Find(plotInfo.Id);
                if (model == null) {
                    return false;
                }

                model.OwnerId = plotInfo.OwnerId;
                model.MapId = plotInfo.MapId;
                model.Number = plotInfo.Number;
                model.ApartmentNumber = plotInfo.ApartmentNumber;
                model.ExpiryTime = plotInfo.ExpiryTime.FromEpochSeconds();
                model.Name = plotInfo.Name;
                Context.UgcMap.Update(model);
            }

            return Context.TrySaveChanges();
        }

        public ICollection<PlotCube>? SaveCubes(PlotInfo plotInfo, IEnumerable<PlotCube> cubes) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            var results = new List<UgcMapCube>();
            var updated = new HashSet<long>();
            foreach (PlotCube cube in cubes) {
                UgcMapCube model = cube;
                model.UgcMapId = plotInfo.Id;
                if (model.Id >= Constant.FurnishingBaseId) {
                    model.Id = 0; // This needs to be auto-generated.
                    results.Add(model);
                    Context.UgcMapCube.Add(model);
                } else {
                    updated.Add(model.Id);
                    results.Add(model);
                    Context.UgcMapCube.Update(model);
                }
            }
            foreach (UgcMapCube cube in Context.UgcMapCube.Where(cube => cube.UgcMapId == plotInfo.Id)) {
                if (!updated.Contains(cube.Id)) {
                    Context.UgcMapCube.Remove(cube);
                }
            }

            if (!Context.TrySaveChanges()) {
                return null;
            }

            PlotCube[] plotCubes = results.Select<UgcMapCube, PlotCube>(cube => cube).ToArray();
            foreach (PlotCube cube in plotCubes) {
                if (!cube.ItemType.IsInteractFurnishing) continue;
                game.itemMetadata.TryGet(cube.ItemId, out ItemMetadata? itemMetadata);
                if (itemMetadata?.Install is null) continue;

                game.functionCubeMetadata.TryGet(itemMetadata.Install.InteractId, out FunctionCubeMetadata? functionCubeMetadata);
                if (functionCubeMetadata?.Nurturing is null) continue;

                cube.Interact!.Nurturing = GetNurturing(plotInfo.OwnerId, cube.ItemId);
            }

            return plotCubes;
        }

        public bool InitUgcMap(IEnumerable<UgcMapMetadata> maps) {
            // If there are entries, we assume it's already initialized.
            if (Context.UgcMap.Any()) {
                return true;
            }

            foreach (UgcMapMetadata map in maps) {
                if (map.Id == Constant.DefaultHomeMapId) {
                    continue;
                }

                foreach (UgcMapGroup group in map.Plots.Values) {
                    Context.UgcMap.Add(new UgcMap {
                        MapId = map.Id,
                        Number = group.Number,
                        ApartmentNumber = group.ApartmentNumber,
                    });
                }
            }

            return Context.TrySaveChanges();
        }

        private Plot? ToPlot(UgcMap? ugcMap) {
            if (ugcMap == null || !game.mapMetadata.TryGetUgc(ugcMap.MapId, out UgcMapMetadata? metadata)) {
                return null;
            }

            if (!metadata.Plots.TryGetValue(ugcMap.Number, out UgcMapGroup? group)) {
                return null;
            }

            var plot = new Plot(group) {
                Id = ugcMap.Id,
                OwnerId = ugcMap.OwnerId,
                MapId = ugcMap.MapId,
                Number = ugcMap.Number,
                ApartmentNumber = 0,
                ExpiryTime = ugcMap.ExpiryTime.ToUnixTimeSeconds(),
            };

            if (ugcMap.Cubes == null) return plot;

            foreach (PlotCube cube in ugcMap.Cubes) {
                if (cube.ItemType.IsInteractFurnishing) {
                    game.itemMetadata.TryGet(cube.ItemId, out ItemMetadata? itemMetadata);
                    if (itemMetadata?.Install is not null) {
                        game.functionCubeMetadata.TryGet(itemMetadata.Install.InteractId, out FunctionCubeMetadata? functionCubeMetadata);
                        if (functionCubeMetadata?.Nurturing is not null) {
                            cube.Interact!.Nurturing = GetNurturing(ugcMap.OwnerId, cube.ItemId);
                        }
                    }
                }

                plot.Cubes.Add(cube.Position, cube);
            }

            return plot;
        }

        private PlotInfo? ToPlotInfo(UgcMap? ugcMap) {
            if (ugcMap == null || !game.mapMetadata.TryGetUgc(ugcMap.MapId, out UgcMapMetadata? metadata)) {
                return null;
            }

            if (!metadata.Plots.TryGetValue(ugcMap.Number, out UgcMapGroup? group)) {
                return null;
            }

            return new PlotInfo(group) {
                Id = ugcMap.Id,
                OwnerId = ugcMap.OwnerId,
                MapId = ugcMap.MapId,
                Number = ugcMap.Number,
                Name = ugcMap.Name,
                ApartmentNumber = 0,
                ExpiryTime = ugcMap.ExpiryTime.ToUnixTimeSeconds(),
            };
        }
    }
}
