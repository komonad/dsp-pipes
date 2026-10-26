using System;
using System.Collections.Generic;
using UnityEngine;

namespace FluidPipes
{
    internal static class PipeTypeNotifications
    {
        private static readonly object changeLock = new object();
        private static HashSet<Tuple<PlanetFactory, int>> pendingChanges =
            new HashSet<Tuple<PlanetFactory, int>>();
        private static HashSet<Tuple<PlanetFactory, int>> dispatchChanges =
            new HashSet<Tuple<PlanetFactory, int>>();

        private static bool dispatchingChanges;
        private static volatile bool cancelDispatch;

        internal static event Action<PlanetFactory, int> ConnectionsChanged;

        internal static void Changed(PlanetFactory factory, int entityId)
        {
            if (factory != null && entityId > 0 && ConnectionsChanged != null)
            {
                lock (changeLock)
                {
                    pendingChanges.Add(Tuple.Create(factory, entityId));
                }
            }
        }

        internal static void FlushChanges()
        {
            if (!PipeTypeRegistry.IsMainThread)
            {
                throw new InvalidOperationException(
                    "Connection events require the Unity main thread."
                );
            }

            HashSet<Tuple<PlanetFactory, int>> completedChanges;
            lock (changeLock)
            {
                if (dispatchingChanges || pendingChanges.Count == 0)
                {
                    return;
                }

                completedChanges = pendingChanges;
                pendingChanges = dispatchChanges;
                dispatchChanges = completedChanges;
                dispatchingChanges = true;
                cancelDispatch = false;
            }
            try
            {
                // 回调中的变更进入下一批次，以保持已完成批次的迭代。
                foreach (var change in completedChanges)
                {
                    if (cancelDispatch)
                    {
                        break;
                    }
                    var observers = ConnectionsChanged;
                    if (observers == null)
                    {
                        continue;
                    }

                    foreach (Action<PlanetFactory, int> handler in observers.GetInvocationList())
                    {
                        if (cancelDispatch)
                        {
                            break;
                        }
                        try
                        {
                            handler(change.Item1, change.Item2);
                        }
                        catch (Exception error)
                        {
                            Debug.LogError("FluidPipes connection observer failed: " + error);
                        }
                    }
                }
            }
            finally
            {
                lock (changeLock)
                {
                    completedChanges.Clear();
                    dispatchingChanges = false;
                    cancelDispatch = false;
                }
            }
        }

        internal static void ClearChanges()
        {
            lock (changeLock)
            {
                pendingChanges.Clear();
                if (dispatchingChanges)
                {
                    cancelDispatch = true;
                }
                else
                {
                    dispatchChanges.Clear();
                }
            }
        }
    }
}
