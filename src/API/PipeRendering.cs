using System;
using UnityEngine;

namespace FluidPipes.API
{
    /// <summary>为扩展绘制的结构附件提供共享渲染资源。</summary>
    public static class PipeRendering
    {
        /// <summary>将管道金属材质使用的天空反射参数写入扩展的绘制属性块。</summary>
        /// <param name="properties">扩展拥有的属性块，其中的其它属性保持原值。</param>
        /// <param name="radial">附件位置的行星径向；零向量使用全局向上方向。</param>
        /// <remarks>在主线程的每次绘制前调用，以同步场景资源的生命周期。</remarks>
        /// <exception cref="ArgumentNullException">properties 为 null。</exception>
        public static void ApplyMetalLighting(MaterialPropertyBlock properties, Vector3 radial)
        {
            if (properties == null)
            {
                throw new ArgumentNullException(nameof(properties));
            }
            PipeVisuals.ApplySteelLighting(properties, radial);
        }
    }
}
