using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FluidPipes.API;
using HarmonyLib;

namespace FluidPipes
{
    internal static class PipeTypeRegistry
    {
        internal sealed class Entry
        {
            internal string Key;
            internal string OwnerGuid;
            internal string Family;
            internal PipeTypeDefinition Definition;
        }

        private static readonly Dictionary<int, Entry> items = new Dictionary<int, Entry>();
        private static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(
            StringComparer.Ordinal
        );
        private static int mainThread;
        private static bool sealedDatabase;
        private static ItemProto template;

        internal static IEnumerable<Entry> Entries => entries.Values;

        internal static bool IsMainThread => Thread.CurrentThread.ManagedThreadId == mainThread;

        internal static void Initialize() => mainThread = Thread.CurrentThread.ManagedThreadId;

        internal static void RequireRegistration()
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThread)
            {
                throw new InvalidOperationException(
                    "Pipe type registration requires the Unity main thread."
                );
            }

            if (sealedDatabase)
            {
                throw new InvalidOperationException(
                    "Pipe type registration is closed after database initialization."
                );
            }
        }

        internal static void Register(string ownerGuid, PipeTypeDefinition definition)
        {
            RequireRegistration();
            if (string.IsNullOrWhiteSpace(ownerGuid))
            {
                throw new ArgumentException("An owner plugin GUID is required.", nameof(ownerGuid));
            }

            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            string key = ownerGuid + ":" + definition.LocalName;
            if (entries.ContainsKey(key) || items.ContainsKey(definition.Prototype.ID))
            {
                throw new InvalidOperationException(
                    "Duplicate pipe type or item binding: " + key + " / " + definition.Prototype.ID
                );
            }

            var entry = new Entry
            {
                Key = key,
                OwnerGuid = ownerGuid,
                Definition = definition,
                Family =
                    definition.ConnectionFamily
                    ?? (
                        definition.Behavior == PipeBehavior.SharedInventory
                            ? "fluidpipes:inventory"
                            : key
                    ),
            };
            entries.Add(key, entry);
            items.Add(definition.Prototype.ID, entry);
        }

        internal static Entry Get(int itemId) =>
            items.TryGetValue(itemId, out var entry) ? entry : null;

        internal static Entry Get(string key) =>
            key != null && entries.TryGetValue(key, out var entry) ? entry : null;

        internal static bool IsItem(int itemId) => Get(itemId) != null;

        internal static bool IsItem(int? itemId) => itemId.HasValue && IsItem(itemId.Value);

        internal static PipeGeometryProfile Geometry(int? itemId) =>
            (itemId.HasValue ? Get(itemId.Value)?.Definition.Geometry : null)
            ?? PipeGeometryProfile.Default;

        internal static bool IsInventory(int? itemId) =>
            itemId.HasValue && IsInventory(itemId.Value);

        internal static bool IsInventory(int itemId) =>
            Get(itemId)?.Definition.Behavior == PipeBehavior.SharedInventory;

        internal static bool IsUndirected(int? itemId) =>
            itemId.HasValue
            && Get(itemId.Value)?.Definition.DirectionMode == PipeDirectionMode.Undirected;

        internal static bool Compatible(int firstId, int secondId)
        {
            var first = Get(firstId);
            var second = Get(secondId);
            return first != null
                && second != null
                && first.Definition.DirectionMode == second.Definition.DirectionMode
                && first.Definition.Behavior == second.Definition.Behavior
                && first.Family == second.Family;
        }

        internal static void InitializeTemplate(ItemProto item) => template = item;

        internal static void Default(ItemProto item) =>
            Register(PipeApi.PluginGuid, new PipeTypeDefinition("default", item));

        internal static ItemProto CreateItem(int itemId, string nameKey, string descriptionKey)
        {
            RequireRegistration();
            if (template == null)
            {
                throw new InvalidOperationException(
                    "Create pipe items during LDBTool's pre-add stage, after the pipe template is initialized."
                );
            }

            if (itemId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(itemId));
            }

            if (string.IsNullOrWhiteSpace(nameKey))
            {
                throw new ArgumentException("A name key is required.", nameof(nameKey));
            }

            if (string.IsNullOrWhiteSpace(descriptionKey))
            {
                throw new ArgumentException(
                    "A description key is required.",
                    nameof(descriptionKey)
                );
            }

            var copy = (ItemProto)
                AccessTools.Method(typeof(object), "MemberwiseClone").Invoke(template, null);
            foreach (var field in typeof(ItemProto).GetFields())
            {
                if (field.GetValue(copy) is Array array)
                {
                    field.SetValue(copy, array.Clone());
                }
            }

            copy.ID = itemId;
            copy.Name = nameKey;
            copy.Description = descriptionKey;
            copy.SID = copy.sid = "";
            copy.BuildIndex = 0;
            copy.Upgrades = new int[0];
            copy.handcrafts = copy.recipes = copy.makes = null;
            copy.rawMats = null;
            return copy;
        }

        internal static void Seal()
        {
            var resolvedItems = new Dictionary<int, Entry>();
            foreach (var entry in entries.Values)
            {
                var prototype = entry.Definition.Prototype;
                if (
                    LDB.items.Select(prototype.ID) != prototype
                    || prototype.prefabDesc == null
                    || !prototype.prefabDesc.isBelt
                )
                {
                    throw new InvalidOperationException(
                        "Pipe type must bind its initialized belt item prototype: " + entry.Key
                    );
                }

                if (resolvedItems.ContainsKey(prototype.ID))
                {
                    throw new InvalidOperationException(
                        "Pipe types resolve to the same item ID: " + prototype.ID
                    );
                }
                resolvedItems.Add(prototype.ID, entry);
            }
            items.Clear();
            foreach (var resolvedItem in resolvedItems)
            {
                items.Add(resolvedItem.Key, resolvedItem.Value);
            }
            sealedDatabase = true;
        }
    }
}
