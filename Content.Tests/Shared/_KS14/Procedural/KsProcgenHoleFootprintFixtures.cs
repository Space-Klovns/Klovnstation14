using System.Linq;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._KS14.Procedural;

public static class KsProcgenHoleFootprintFixtures
{
    public static KsProcgenRequest Create(bool transpose = false, bool threeBands = false, bool separateNetworks = false, int smallRoomCount = 1)
    {
        var width = threeBands ? 10 + 4 * smallRoomCount : 10;
        bool Hole(int x, int y) => y is >= 2 and <= 4 &&
            (threeBands ? x is 6 or 7 || x >= 11 && ((x - 11) % 4 is 0 or 1) : x is 4 or 5);
        Vector2i Pose(int x, int y) => transpose ? new(y, x) : new(x, y);
        var request = new KsProcgenRequest
        {
            SchemaVersion = 2, RequestId = "CourtyardRooms", Seed = 17,
            ConnectivityPolicy = KsProcgenConnectivityPolicy.PerIsland, EntranceDomain = new(),
            WindowGoal = new() { ExteriorWindowFraction = 0f },
            SizeMix = threeBands ? new()
            {
                new() { Id = "Large", MinCells = 35, MaxCells = 35 },
                new() { Id = "Medium", MinCells = 23, MaxCells = 23 },
                new() { Id = "Small", MinCells = 16, MaxCells = 16, TargetCount = smallRoomCount },
            } : new()
            {
                new() { Id = "Large", MinCells = 33, MaxCells = 33 },
                new() { Id = "Small", MinCells = 28, MaxCells = 28 },
            },
        };
        foreach (var shift in separateNetworks ? new[] { 0, 20 } : new[] { 0 })
        {
            for (var x = 0; x < width; x++)
            for (var y = 0; y < 7; y++)
            {
                if (Hole(x, y)) request.Shape.VoidCells.Add(Pose(x + shift, y));
                else request.Shape.Cells.Add(Pose(x + shift, y));
            }
            request.Shape.EnvelopeCells.AddRange(Enumerable.Range(-1, width + 2)
                .SelectMany(x => Enumerable.Range(-1, 9).Where(y => x == -1 || x == width || y is -1 or 7)
                    .Select(y => Pose(x + shift, y))));
        }
        if (separateNetworks)
            foreach (var goal in request.SizeMix) goal.TargetCount *= 2;
        return request;
    }
}
