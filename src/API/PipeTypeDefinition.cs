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

        internal Func<PlanetFactory, int, int> BatchGroup { get; }

        /// <summary>通过七参数二进制接口以标准几何定义由物品承载的程序化连接。</summary>
        /// <param name="localName">包含小写 ASCII 字母、数字、点、下划线或连字符的稳定局部标识符。</param>
        /// <param name="prototype">由拥有者独立注册、具有正数 ID 的物品原型。</param>
        /// <param name="appearance">表面设置；为 null 时继承默认外观。开放表面要求结构内芯。</param>
        /// <param name="behavior">共享库存或外部模拟的结构连接。</param>
        /// <param name="connectionFamily">精确的家族键；为 null 时使用通用流体家族或该类型的结构键。</param>
        /// <param name="batchGroup">纯主线程函数，为每个实体返回稳定的渲染分组；在几何放置期间求值。</param>
        /// <param name="directionMode">连接方向；共享库存使用对称连接。</param>
        /// <exception cref="ArgumentNullException">原型为 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException">原型 ID、行为或方向模式无效。</exception>
        /// <exception cref="ArgumentException">局部名称无效，或行为、方向与外观的组合无效。</exception>
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

        /// <summary>定义由物品承载的程序化连接。</summary>
        /// <param name="localName">包含小写 ASCII 字母、数字、点、下划线或连字符的稳定局部标识符。</param>
        /// <param name="prototype">由拥有者独立注册、具有正数 ID 的物品原型。</param>
        /// <param name="appearance">表面设置；为 null 时继承默认外观。开放表面要求结构内芯。无向内芯在横向 X 反转下逐一匹配构件，同时保持径向 Y。</param>
        /// <param name="behavior">共享库存或外部模拟的结构连接。</param>
        /// <param name="connectionFamily">精确的家族键；为 null 时使用通用流体家族或该类型的结构键。</param>
        /// <param name="batchGroup">纯主线程函数，为每个实体返回稳定的渲染分组；在几何放置期间求值。</param>
        /// <param name="directionMode">连接方向；共享库存使用对称连接。</param>
        /// <param name="geometry">管道尺寸；为 null 时使用 <see cref="PipeGeometryProfile.Default"/>。</param>
        /// <exception cref="ArgumentNullException">原型为 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException">原型 ID、行为或方向模式无效。</exception>
        /// <exception cref="ArgumentException">局部名称无效，或行为、方向与外观的组合无效。</exception>
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
