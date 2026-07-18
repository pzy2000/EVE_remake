using System;
using System.Collections.Generic;
using Starfall.Domain;

namespace Starfall.Simulation
{
    public static class UniverseRoutes
    {
        public static IReadOnlyList<string> FindRoute(GeneratedUniverse universe, string fromSystemId, string toSystemId)
        {
            if (universe == null) throw new ArgumentNullException(nameof(universe));
            if (!universe.Systems.ContainsKey(fromSystemId) || !universe.Systems.ContainsKey(toSystemId)) return null;
            if (fromSystemId == toSystemId) return new[] { fromSystemId };

            var previous = new Dictionary<string, string>(StringComparer.Ordinal) { { fromSystemId, null } };
            var queue = new Queue<string>();
            queue.Enqueue(fromSystemId);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var neighbor in universe.Adjacency[current])
                {
                    if (previous.ContainsKey(neighbor)) continue;
                    previous.Add(neighbor, current);
                    if (neighbor == toSystemId)
                    {
                        var route = new List<string> { toSystemId };
                        var cursor = current;
                        while (cursor != null)
                        {
                            route.Insert(0, cursor);
                            cursor = previous[cursor];
                        }
                        return route;
                    }
                    queue.Enqueue(neighbor);
                }
            }
            return null;
        }
    }
}
