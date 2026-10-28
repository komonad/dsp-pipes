using System;

namespace FluidPipes.API
{
    /// <summary>绑定到独立注册的游戏物品的连接类型。</summary>
    public sealed class PipeTypeDefinition
    {
        /// <summary>局部稳定类型名。</summary>
        public string LocalName { get; }

        /// <summary>由拥有者注册的游戏物品原型。</summary>
        public ItemProto Prototype { get; }

        /// <summary>模拟类别。</summary>
        public PipeBehavior Behavior { get; }

        /// <summary>建造与连接方向策略。</summary>
        public PipeDirectionMode DirectionMode { get; }

        /// <summary>精确的连接家族键；同一家族内兼容的行为与方向模式可以连接。</summary>
        public string ConnectionFamily { get; }

        /// <summary>不可变的表面设置。</summary>
        public PipeAppearance Appearance { get; }

        /// <summary>供渲染与连接几何使用的不可变世界空间尺寸。</summary>
        public PipeGeometryProfile Geometry { get; }

        /// <summary>规划与碰撞行为的提供方。</summary>
        public PipeConstructionMode ConstructionMode { get; }

        internal Func<PlanetFactory, int, int> BatchGroup { get; }

        /// <summary>定义参与无向共享库存的管道类型。</summary>
        /// <param name="localName">稳定的局部类型名。</param>
        /// <param name="prototype">扩展独立注册的正数 ID 物品原型。</param>
        /// <param name="appearance">不可变外观；null 使用默认管道外观。</param>
        /// <param name="connectionFamily">共享连接家族；null 使用通用库存家族。</param>
        /// <param name="geometry">世界空间尺寸；null 使用标准管道尺寸。</param>
        /// <returns>供 PipeTypes.Register 使用的不可变定义。</returns>
        /// <exception cref="ArgumentNullException">prototype 为 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException">物品 ID 无效。</exception>
        /// <exception cref="ArgumentException">名称或外观与共享库存规则不符。</exception>
        public static PipeTypeDefinition CreateSharedInventory(
            string localName,
            ItemProto prototype,
            PipeAppearance appearance = null,
            string connectionFamily = null,
            PipeGeometryProfile geometry = null
        ) =>
            new PipeTypeDefinition(
                localName,
                prototype,
                appearance,
                PipeBehavior.SharedInventory,
                connectionFamily,
                null,
                PipeDirectionMode.Undirected,
                geometry,
                PipeConstructionMode.Pipe
            );

        /// <summary>定义由扩展维护运行行为的程序化管道结构。</summary>
        /// <param name="localName">稳定的局部类型名。</param>
        /// <param name="prototype">扩展独立注册的正数 ID 物品原型。</param>
        /// <param name="appearance">不可变外观；开放管壁需要结构截面。</param>
        /// <param name="directionMode">连接方向；默认无向。</param>
        /// <param name="constructionMode">规划与碰撞流程；NativeBelt 要求 Directed。</param>
        /// <param name="connectionFamily">连接家族；null 使用该类型的完整键。</param>
        /// <param name="batchGroup">主线程纯函数，提供稳定的渲染语义分组。</param>
        /// <param name="geometry">世界空间尺寸；null 使用标准管道尺寸。</param>
        /// <returns>供 PipeTypes.Register 使用的不可变定义。</returns>
        /// <exception cref="ArgumentNullException">prototype 为 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException">枚举参数或物品 ID 无效。</exception>
        /// <exception cref="ArgumentException">名称、外观对称性或建造与方向组合无效。</exception>
        public static PipeTypeDefinition CreateStructure(
            string localName,
            ItemProto prototype,
            PipeAppearance appearance = null,
            PipeDirectionMode directionMode = PipeDirectionMode.Undirected,
            PipeConstructionMode constructionMode = PipeConstructionMode.Pipe,
            string connectionFamily = null,
            Func<PlanetFactory, int, int> batchGroup = null,
            PipeGeometryProfile geometry = null
        ) =>
            new PipeTypeDefinition(
                localName,
                prototype,
                appearance,
                PipeBehavior.Structure,
                connectionFamily,
                batchGroup,
                directionMode,
                geometry,
                constructionMode
            );

        private PipeTypeDefinition(
            string localName,
            ItemProto prototype,
            PipeAppearance appearance,
            PipeBehavior behavior,
            string connectionFamily,
            Func<PlanetFactory, int, int> batchGroup,
            PipeDirectionMode directionMode,
            PipeGeometryProfile geometry,
            PipeConstructionMode constructionMode
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
                directionMode == PipeDirectionMode.Undirected
                && appearance?.WallLayout != null
                && !appearance.WallLayout.IsReversalSymmetric
            )
            {
                throw new ArgumentException(
                    "Undirected wall bands use centered bands or adjacent mirrored pairs.",
                    nameof(appearance)
                );
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

            if (!Enum.IsDefined(typeof(PipeConstructionMode), constructionMode))
            {
                throw new ArgumentOutOfRangeException(nameof(constructionMode));
            }
            if (
                constructionMode == PipeConstructionMode.NativeBelt
                && (
                    behavior != PipeBehavior.Structure
                    || directionMode != PipeDirectionMode.Directed
                )
            )
            {
                throw new ArgumentException(
                    "Native belt construction requires a directed structure.",
                    nameof(constructionMode)
                );
            }
            ConstructionMode = constructionMode;
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
