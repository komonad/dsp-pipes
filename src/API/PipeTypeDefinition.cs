using System;

namespace FluidPipes.API
{
    /// <summary>A connection type bound to an independently registered game item.</summary>
    public sealed class PipeTypeDefinition
    {
        /// <summary>The local stable type name.</summary>
        public string LocalName { get; }

        /// <summary>The game item prototype registered by its owner.</summary>
        public ItemProto Prototype { get; }

        /// <summary>The simulation category.</summary>
        public PipeBehavior Behavior { get; }

        /// <summary>The construction and connection orientation policy.</summary>
        public PipeDirectionMode DirectionMode { get; }

        /// <summary>The exact connection-family key; compatible behavior and direction modes within a family may connect.</summary>
        public string ConnectionFamily { get; }

        /// <summary>The immutable surface settings.</summary>
        public PipeAppearance Appearance { get; }

        /// <summary>The immutable world-space dimensions used by rendering and connection geometry.</summary>
        public PipeGeometryProfile Geometry { get; }

        internal Func<PlanetFactory, int, int> BatchGroup { get; }

        /// <summary>Defines an item-backed procedural connection with the standard geometry through the seven-parameter binary interface.</summary>
        /// <param name="localName">A stable local identifier containing lower-case ASCII letters, digits, dot, underscore or hyphen.</param>
        /// <param name="prototype">The independently registered item prototype with a positive ID.</param>
        /// <param name="appearance">Surface settings; null inherits the default appearance. Open surfaces require a structural interior.</param>
        /// <param name="behavior">Shared inventory or externally simulated structural connections.</param>
        /// <param name="connectionFamily">An exact family key; null uses the common fluid family or the type's structural key.</param>
        /// <param name="batchGroup">A pure main-thread function returning a stable rendering group for each entity; evaluated during geometry placement.</param>
        /// <param name="directionMode">The connection orientation; shared inventories use symmetric connections.</param>
        /// <exception cref="ArgumentNullException">The prototype is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A prototype ID, behavior or direction mode is invalid.</exception>
        /// <exception cref="ArgumentException">The local name or combination of behavior, direction and appearance is invalid.</exception>
        public PipeTypeDefinition(
            string localName,
            ItemProto prototype,
            PipeAppearance appearance,
            PipeBehavior behavior,
            string connectionFamily,
            Func<PlanetFactory, int, int> batchGroup,
            PipeDirectionMode directionMode
        )
            : this(
                localName,
                prototype,
                appearance,
                behavior,
                connectionFamily,
                batchGroup,
                directionMode,
                null
            ) { }

        /// <summary>Defines an item-backed procedural connection.</summary>
        /// <param name="localName">A stable local identifier containing lower-case ASCII letters, digits, dot, underscore or hyphen.</param>
        /// <param name="prototype">The independently registered item prototype with a positive ID.</param>
        /// <param name="appearance">Surface settings; null inherits the default appearance. Open surfaces require a structural interior. Undirected interiors match members under lateral X reversal while preserving radial Y.</param>
        /// <param name="behavior">Shared inventory or externally simulated structural connections.</param>
        /// <param name="connectionFamily">An exact family key; null uses the common fluid family or the type's structural key.</param>
        /// <param name="batchGroup">A pure main-thread function returning a stable rendering group for each entity; evaluated during geometry placement.</param>
        /// <param name="directionMode">The connection orientation; shared inventories use symmetric connections.</param>
        /// <param name="geometry">Pipe dimensions; null uses <see cref="PipeGeometryProfile.Default"/>.</param>
        /// <exception cref="ArgumentNullException">The prototype is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A prototype ID, behavior or direction mode is invalid.</exception>
        /// <exception cref="ArgumentException">The local name or combination of behavior, direction and appearance is invalid.</exception>
        public PipeTypeDefinition(
            string localName,
            ItemProto prototype,
            PipeAppearance appearance = null,
            PipeBehavior behavior = PipeBehavior.SharedInventory,
            string connectionFamily = null,
            Func<PlanetFactory, int, int> batchGroup = null,
            PipeDirectionMode directionMode = PipeDirectionMode.Undirected,
            PipeGeometryProfile geometry = null
        )
        {
            if (string.IsNullOrWhiteSpace(localName))
            {
                throw new ArgumentException("A local type name is required.", nameof(localName));
            }

            foreach (char character in localName)
            {
                if (
                    !(character >= 'a' && character <= 'z')
                    && !(character >= '0' && character <= '9')
                    && character != '.'
                    && character != '_'
                    && character != '-'
                )
                {
                    throw new ArgumentException(
                        "Type names use lower-case ASCII letters, digits, dot, underscore or hyphen.",
                        nameof(localName)
                    );
                }
            }

            if (prototype == null)
            {
                throw new ArgumentNullException(nameof(prototype));
            }

            if (prototype.ID <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(prototype));
            }

            if (!Enum.IsDefined(typeof(PipeBehavior), behavior))
            {
                throw new ArgumentOutOfRangeException(nameof(behavior));
            }

            if (!Enum.IsDefined(typeof(PipeDirectionMode), directionMode))
            {
                throw new ArgumentOutOfRangeException(nameof(directionMode));
            }

            if (
                behavior == PipeBehavior.SharedInventory
                && directionMode == PipeDirectionMode.Directed
            )
            {
                throw new ArgumentException(
                    "Shared inventory types use undirected connections.",
                    nameof(directionMode)
                );
            }

            if (appearance?.Interior != null)
            {
                if (behavior != PipeBehavior.Structure)
                {
                    throw new ArgumentException(
                        "Interior cross-sections belong to structural pipe types.",
                        nameof(appearance)
                    );
                }
                if (
                    directionMode == PipeDirectionMode.Undirected
                    && !appearance.Interior.IsReversalSymmetric
                )
                {
                    throw new ArgumentException(
                        "Undirected interiors have matching sections at (x, y) and (-x, y).",
                        nameof(appearance)
                    );
                }
            }

            if (
                appearance?.WallMode == PipeWallMode.Open
                && (behavior != PipeBehavior.Structure || appearance.Interior == null)
            )
            {
                throw new ArgumentException(
                    "Open surfaces require a structural interior profile.",
                    nameof(appearance)
                );
            }

            DirectionMode = directionMode;
            LocalName = localName;
            Prototype = prototype;
            Appearance = appearance;
            Geometry = geometry ?? PipeGeometryProfile.Default;
            Behavior = behavior;
            ConnectionFamily = connectionFamily;
            BatchGroup = batchGroup;
        }
    }
}
