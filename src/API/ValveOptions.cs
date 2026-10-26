using System;

namespace FluidPipes.API
{
    /// <summary>阀门原生端口 A 与 B 之间允许的传输方向。</summary>
    public enum FlowDirection
    {
        /// <summary>两个方向均可传输。</summary>
        Both,

        /// <summary>仅允许从 A 到 B 的传输。</summary>
        AtoB,

        /// <summary>仅允许从 B 到 A 的传输。</summary>
        BtoA,
    }

    /// <summary>用于选择传输方向与传输量的填充水平目标。</summary>
    public enum LevelControl
    {
        /// <summary>减小两侧的填充比例差。</summary>
        Equalize,

        /// <summary>将 A 侧维持在目标比例。</summary>
        KeepA,

        /// <summary>将 B 侧维持在目标比例。</summary>
        KeepB,
    }

    /// <summary>不可变的泵设置。速率为物品/秒；目标为 0 到 1 之间的容量比例。</summary>
    public sealed class ValveOptions
    {
        /// <summary>阀门是否可以请求电力并传输物品。</summary>
        public bool Enabled { get; }

        /// <summary>按可用电力缩放前的最大传输速率。</summary>
        public double ItemsPerSecond { get; }

        /// <summary>所选侧期望的填充比例，取值为 0 到 1。</summary>
        public double TargetFraction { get; }

        /// <summary>允许的传输方向。</summary>
        public FlowDirection Direction { get; }

        /// <summary>填充水平目标。</summary>
        public LevelControl Control { get; }

        /// <summary>构造一组不可变且经过校验的泵设置。</summary>
        /// <param name="enabled">是否启用传输。</param>
        /// <param name="itemsPerSecond">位于闭区间 0–1,000,000 内的有限最大速率。</param>
        /// <param name="targetFraction">位于闭区间 0–1 内的有限目标填充比例。</param>
        /// <param name="direction">允许的传输方向。</param>
        /// <param name="control">填充水平目标。</param>
        /// <exception cref="ArgumentOutOfRangeException">速率、比例或枚举值超出其取值范围。</exception>
        public ValveOptions(
            bool enabled = true,
            double itemsPerSecond = 60,
            double targetFraction = 0.5,
            FlowDirection direction = FlowDirection.Both,
            LevelControl control = LevelControl.Equalize
        )
        {
            if (
                double.IsNaN(itemsPerSecond)
                || double.IsInfinity(itemsPerSecond)
                || itemsPerSecond < 0
                || itemsPerSecond > 1000000
            )
            {
                throw new ArgumentOutOfRangeException(nameof(itemsPerSecond));
            }

            if (
                double.IsNaN(targetFraction)
                || double.IsInfinity(targetFraction)
                || targetFraction < 0
                || targetFraction > 1
            )
            {
                throw new ArgumentOutOfRangeException(nameof(targetFraction));
            }

            if (direction < FlowDirection.Both || direction > FlowDirection.BtoA)
            {
                throw new ArgumentOutOfRangeException(nameof(direction));
            }

            if (control < LevelControl.Equalize || control > LevelControl.KeepB)
            {
                throw new ArgumentOutOfRangeException(nameof(control));
            }

            Enabled = enabled;
            ItemsPerSecond = itemsPerSecond;
            TargetFraction = targetFraction;
            Direction = direction;
            Control = control;
        }
    }
}
