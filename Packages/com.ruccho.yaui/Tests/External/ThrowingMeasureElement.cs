using System;
using UnityEngine;

namespace Yaui.Tests.External
{
    /// <summary>An element whose measurement throws, on the layout thread.</summary>
    [AddComponentMenu("")]
    public class ThrowingMeasureElement : YauiElement
    {
        protected override bool MeasuresContent => true;

        protected override Vector2 MeasureContent(float width, YauiMeasureMode widthMode, float height,
            YauiMeasureMode heightMode)
        {
            throw new InvalidOperationException("measure");
        }
    }
}
