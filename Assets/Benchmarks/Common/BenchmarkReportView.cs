using UnityEngine;

namespace Yaui.Benchmarks
{
    /// <summary>
    /// Shows the benchmark report on screen after all scenarios have finished.
    /// </summary>
    public sealed class BenchmarkReportView : MonoBehaviour
    {
        Vector2 _scroll;

        public string Report { get; set; }

        void OnGUI()
        {
            var scale = Mathf.Max(1f, Screen.dpi / 160f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var area = new Rect(8, 8, Screen.width / scale - 16, Screen.height / scale - 16);

            GUILayout.BeginArea(area, GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.Label(Report);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
