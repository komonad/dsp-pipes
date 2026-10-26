using System;

namespace FluidPipes.API
{
    /// <summary>生产机器连接处的传输方向。</summary>
    public enum MachinePortDirection
    {
        /// <summary>从管道库存向机器的配方输入缓冲供给。</summary>
        IntoMachine,

        /// <summary>将机器的配方产物收集到管道库存中。</summary>
        OutOfMachine,
    }

    /// <summary>单个机器插槽的不可变方向、物品过滤器与启用状态。</summary>
    public sealed class MachinePortOptions
    {
        /// <summary>分配给此连接的配方物品；为零表示等待物品选择。</summary>
        public int ItemId { get; }

        /// <summary>相对于机器的传输方向。</summary>
        public MachinePortDirection Direction { get; }

        /// <summary>已配置的连接是否参与传输与物品预留。</summary>
        public bool Enabled { get; }

        /// <summary>创建连接设置。应用时会检查配方与网络的兼容性。</summary>
        /// <param name="itemId">正数物品 ID，或为零以等待物品选择。</param>
        /// <param name="direction">相对于机器的传输方向。</param>
        /// <param name="enabled">连接是否参与传输与物品预留。</param>
        /// <exception cref="ArgumentOutOfRangeException">物品 ID 或方向无效。</exception>
        public MachinePortOptions(
            int itemId = 0,
            MachinePortDirection direction = MachinePortDirection.IntoMachine,
            bool enabled = true
        )
        {
            if (itemId < 0 || !Enum.IsDefined(typeof(MachinePortDirection), direction))
            {
                throw new ArgumentOutOfRangeException(
                    itemId < 0 ? nameof(itemId) : nameof(direction)
                );
            }

            ItemId = itemId;
            Direction = direction;
            Enabled = enabled;
        }
    }

    /// <summary>原生机器插槽连接的独立视图。</summary>
    public readonly struct MachinePortSnapshot
    {
        /// <summary>管道实体 ID，或负数形式的待建对象 ID。</summary>
        public int PipeEntityId { get; }

        /// <summary>机器实体 ID，或负数形式的待建对象 ID。</summary>
        public int MachineEntityId { get; }

        /// <summary>机器上从零开始计数的原生分拣器插槽。</summary>
        public int SlotIndex { get; }

        /// <summary>当前分配给该连接的不可变设置。</summary>
        public MachinePortOptions Options { get; }

        internal MachinePortSnapshot(PipeMachinePorts.Connection connection)
        {
            PipeEntityId = connection.PipeEntityId;
            MachineEntityId = connection.MachineEntityId;
            SlotIndex = connection.SlotIndex;
            Options = connection.Options;
        }
    }
}
