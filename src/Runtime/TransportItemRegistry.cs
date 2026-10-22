using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace FluidPipes
{
    internal static class TransportItemRegistry
    {
        private sealed class Registration : IDisposable
        {
            internal readonly int[] Items;
            private bool disposed;

            internal Registration(int[] items)
            {
                Items = items;
            }

            public void Dispose()
            {
                RequireMainThread();
                lock (sync)
                {
                    if (disposed)
                    {
                        return;
                    }

                    disposed = true;
                    registrations.Remove(this);
                    if (ready)
                    {
                        Rebuild();
                    }
                }
            }
        }

        private static readonly object sync = new object();
        private static readonly List<Registration> registrations = new List<Registration>();
        private static volatile ItemAdmission admission = new ItemAdmission(
            new int[0],
            new int[0],
            new int[0]
        );
        private static int[] allowed = new int[0];
        private static int[] denied = new int[0];
        private static bool includeFluids = true;
        private static bool ready;
        private static int mainThread;
        private static int version;

        internal static int Version => Volatile.Read(ref version);

        internal static bool IsAllowed(int itemId) => admission.Allows(itemId);

        internal static void Configure(bool fluids, int[] additions, int[] exclusions)
        {
            mainThread = Thread.CurrentThread.ManagedThreadId;
            lock (sync)
            {
                includeFluids = fluids;
                allowed = (int[])additions.Clone();
                denied = (int[])exclusions.Clone();
                if (ready)
                {
                    Rebuild();
                }
            }
        }

        internal static void Refresh()
        {
            RequireMainThread();
            lock (sync)
            {
                ready = true;
                Rebuild();
            }
        }

        internal static IDisposable Register(int[] itemIds)
        {
            RequireMainThread();
            if (itemIds == null)
            {
                throw new ArgumentNullException(nameof(itemIds));
            }

            if (itemIds.Any(itemId => itemId <= 0))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(itemIds),
                    "Item IDs must be positive."
                );
            }

            var registration = new Registration(itemIds.Distinct().ToArray());
            lock (sync)
            {
                registrations.Add(registration);
                if (ready)
                {
                    Rebuild();
                }
            }
            return registration;
        }

        private static void Rebuild()
        {
            var items = LDB.items.dataArray;
            var known = new HashSet<int>(items.Select(item => item.ID));
            var additions = allowed
                .Concat(registrations.SelectMany(registration => registration.Items))
                .Where(known.Contains);
            admission = new ItemAdmission(
                items.Where(item => includeFluids && item.IsFluid).Select(item => item.ID),
                additions,
                denied
            );
            Interlocked.Increment(ref version);
        }

        private static void RequireMainThread()
        {
            if (mainThread != 0 && mainThread != Thread.CurrentThread.ManagedThreadId)
            {
                throw new InvalidOperationException(
                    "Transport item registrations require the Unity main thread."
                );
            }
        }
    }
}
