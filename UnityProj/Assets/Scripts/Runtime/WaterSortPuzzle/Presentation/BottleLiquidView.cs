using UnityEngine;
using UnityEngine.UI;

namespace Game.WaterSortPuzzle
{
    [RequireComponent(typeof(Image))]
    public sealed class BottleLiquidView : MonoBehaviour
    {
        private const float WAVEAMPLITUDE = 0.01f; // 倒水时的波纹振幅。
        private const float HIGHLIGHTHEIGHT = 0.02f; // 静止液面的高光厚度。
        private static readonly int[] m_layerColorIDs =
        {
            Shader.PropertyToID("_Color1"),
            Shader.PropertyToID("_Color2"),
            Shader.PropertyToID("_Color3"),
            Shader.PropertyToID("_Color4")
        }; // 从瓶底到瓶口排列的颜色属性。
        private static readonly int m_surfaceColorID = Shader.PropertyToID("_SurfaceColor"); // 表面颜色属性。
        private static readonly int m_fillAmountID = Shader.PropertyToID("_FillAmount"); // 液体层数属性。
        private static readonly int m_rotationID = Shader.PropertyToID("_Rotation"); // 瓶身世界旋转属性。
        private static readonly int m_waveAmplitudeID = Shader.PropertyToID("_WaveAmplitude"); // 波纹振幅属性。
        private static readonly int m_highlightHeightID = Shader.PropertyToID("_EllipseHeight"); // 高光厚度属性。
        private static readonly int m_hiddenCountID = Shader.PropertyToID("_TexLayerCount"); // 问号层数属性。
        // 最上方问号层的揭晓进度属性。
        private static readonly int m_revealProgressID = Shader.PropertyToID("_TexLayerSwitchProgress");
        private Image m_image; // 使用瓶身内部轮廓的液体图像。
        private Material m_sharedMaterial; // 预制体上指定的材质模板。
        private Material m_material; // 当前瓶子独占的材质实例。

        /// <summary>按瓶子数据同步各层水体颜色、液面颜色与隐藏层数。</summary>
        /// <param name="bottle">当前瓶子的数据。</param>
        public void RefreshColors(BottleData bottle)
        {
            // 未装水的层使用透明色，避免复用瓶子时显示残留颜色。
            for (int index = 0; index < BottleData.CAPACITY; index++)
            {
                int colorID = index < bottle.Colors.Count ? bottle.Colors[index] : 0;
                SetColor(m_layerColorIDs[index], WaterSortPalette.GetBody(colorID));
            }

            SetColor(m_surfaceColorID, WaterSortPalette.GetSurface(bottle.TopColor));
            SetHidden(bottle.InvisibleCount, 0f);
        }

        /// <summary>更新液面高度。</summary>
        /// <param name="layers">当前液体层数，允许使用小数层。</param>
        public void SetFill(float layers)
        {
            SetFloat(m_fillAmountID, Mathf.Clamp(layers, 0f, BottleData.CAPACITY));
        }

        /// <summary>更新瓶身相对于水平面的旋转。</summary>
        /// <param name="degrees">瓶子的 Z 轴旋转角度。</param>
        public void SetRotation(float degrees)
        {
            SetFloat(m_rotationID, degrees);
        }

        /// <summary>设置液面波纹状态。</summary>
        /// <param name="enabled">是否绘制液面波纹。</param>
        public void SetWave(bool enabled)
        {
            SetFloat(m_waveAmplitudeID, enabled ? WAVEAMPLITUDE : 0f);
        }

        /// <summary>设置液面高光状态。</summary>
        /// <param name="enabled">是否绘制液面高光。</param>
        public void SetHighlight(bool enabled)
        {
            SetFloat(m_highlightHeightID, enabled ? HIGHLIGHTHEIGHT : 0f);
        }

        /// <summary>设置隐藏层数与最上方隐藏层的揭晓程度。</summary>
        /// <param name="count">从瓶底开始计数的隐藏层数。</param>
        /// <param name="revealProgress">最上方隐藏层从问号变为真实颜色的进度。</param>
        public void SetHidden(int count, float revealProgress)
        {
            SetFloat(m_hiddenCountID, Mathf.Clamp(count, 0, BottleData.CAPACITY));
            SetFloat(m_revealProgressID, Mathf.Clamp01(revealProgress));
        }

        private void OnDestroy()
        {
            if (m_material == null)
                return;

            // 先恢复材质引用，让 uGUI 释放基于实例创建的遮罩派生材质。
            if (m_image != null)
                m_image.material = m_sharedMaterial;

            Destroy(m_material);
        }

        private void EnsureMaterial()
        {
            if (m_material != null)
                return;

            // 池化对象首次刷新时即可写入属性，包括尚未激活的瓶子。
            m_image = GetComponent<Image>();
            m_sharedMaterial = m_image.material;
            m_material = new Material(m_sharedMaterial);
            m_image.material = m_material;
        }

        private void SetFloat(int propertyID, float value)
        {
            EnsureMaterial();
            m_material.SetFloat(propertyID, value);

            // 使用父级 Mask 时，uGUI 绘制的是基于当前材质建立的模板测试副本。
            Material renderingMaterial = m_image.materialForRendering;
            if (renderingMaterial != m_material)
                renderingMaterial.SetFloat(propertyID, value);
        }

        private void SetColor(int propertyID, Color value)
        {
            EnsureMaterial();
            m_material.SetColor(propertyID, value);

            Material renderingMaterial = m_image.materialForRendering;
            if (renderingMaterial != m_material)
                renderingMaterial.SetColor(propertyID, value);
        }
    }
}
