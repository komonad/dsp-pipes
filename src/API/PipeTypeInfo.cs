namespace FluidPipes.API
{
    /// <summary>已注册类型的独立标识快照。</summary>
    public readonly struct PipeTypeInfo
    {
        /// <summary>限定的拥有者与局部类型键。</summary>
        public string Key { get; }

        /// <summary>注册该类型的插件 GUID。</summary>
        public string OwnerGuid { get; }

        /// <summary>已初始化的游戏物品 ID。</summary>
        public int ItemId { get; }

        /// <summary>模拟类别。</summary>
        public PipeBehavior Behavior { get; }

        /// <summary>建造与连接方向策略。</summary>
        public PipeDirectionMode DirectionMode { get; }

        /// <summary>连接家族。</summary>
        public string ConnectionFamily { get; }

        /// <summary>已注册的截面尺寸与中心高度，供附件和外部接口定位。</summary>
        public PipeGeometryProfile Geometry { get; }

        /// <summary>类型采用的规划与碰撞流程。</summary>
        public PipeConstructionMode ConstructionMode { get; }

        /// <summary>类型注册时定义的不可变外观设置。</summary>
        public PipeAppearance Appearance { get; }

        internal PipeTypeInfo(PipeTypeRegistry.Entry entry)
        {
            Key = entry.Key;
            OwnerGuid = entry.OwnerGuid;
            ItemId = entry.Definition.Prototype.ID;
            Behavior = entry.Definition.Behavior;
            DirectionMode = entry.Definition.DirectionMode;
            ConnectionFamily = entry.Family;
            Geometry = entry.Definition.Geometry;
            ConstructionMode = entry.Definition.ConstructionMode;
            Appearance = entry.Definition.Appearance;
        }
    }
}
