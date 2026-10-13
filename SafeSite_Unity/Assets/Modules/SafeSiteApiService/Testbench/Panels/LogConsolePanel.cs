using System.Collections.Generic;
using System.Text;
using Modules.SafeSiteApiService.External;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Modules.SafeSiteApiService.Testbench
{
    /// <summary>Every API call: time, method, path, status, ms. Click an entry for its bodies.</summary>
    public sealed class LogConsolePanel : TestbenchPanel
    {
        private const int MaxEntries = 400;

        private readonly List<(ApiLogEntry entry, GameObject view)> _entries = new List<(ApiLogEntry, GameObject)>();
        private RectTransform _list;
        private ScrollRect _scroll;
        private TextMeshProUGUI _count;
        private bool _scrollToEnd;

        public override string Title => "Log";

        protected override void Build(RectTransform content)
        {
            var row = Ui.Row(content);
            Ui.Label(row, "Log", Ui.FontSize + 2, Ui.Text, 60, FontStyles.Bold);
            Ui.Button(row, "Clear", Clear);
            Ui.Button(row, "Copy all", CopyAll);
            _count = Ui.Label(row, "", color: Ui.Muted);

            _list = Ui.ScrollView(content, out _scroll, "LogScroll");
            Ui.Layout(_scroll, minHeight: 80, flexibleHeight: 1);
            ((VerticalLayoutGroup)_list.GetComponent<LayoutGroup>()).spacing = 2;

            Service.Logged += Add;
        }

        protected override void OnDestroy()
        {
            if (State != null)
                Service.Logged -= Add;
            base.OnDestroy();
        }

        private void Add(ApiLogEntry entry)
        {
            var atEnd = _scroll.verticalNormalizedPosition <= 0.02f || _entries.Count == 0;

            var view = Ui.Column(_list, 2, 0, "Entry");
            Ui.Fill(view, Ui.Field);
            var header = Ui.Label(view, Summary(entry), Ui.FontSize - 2, entry.IsError ? Ui.Bad : Ui.Text);
            header.margin = new Vector4(8, 2, 8, 2);
            header.textWrappingMode = TextWrappingModes.NoWrap;
            header.overflowMode = TextOverflowModes.Ellipsis;
            var details = Ui.Label(view, Details(entry), Ui.FontSize - 4, Ui.Muted);
            details.margin = new Vector4(16, 0, 8, 6);
            details.richText = false;
            details.gameObject.SetActive(false);

            var button = view.gameObject.AddComponent<Button>();
            button.targetGraphic = view.GetComponent<Image>();
            button.onClick.AddListener(() => details.gameObject.SetActive(!details.gameObject.activeSelf));

            _entries.Add((entry, view.gameObject));
            while (_entries.Count > MaxEntries)
            {
                Destroy(_entries[0].view);
                _entries.RemoveAt(0);
            }

            _count.text = $"{_entries.Count} entries";
            _scrollToEnd |= atEnd;
        }

        private void LateUpdate()
        {
            if (!_scrollToEnd)
                return;
            _scrollToEnd = false;
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 0f;
        }

        private static string Summary(ApiLogEntry entry) =>
            $"{entry.Time:HH:mm:ss}  {entry.Method,-5} {entry.Path}  {(entry.Status == 0 ? "ERR" : entry.Status.ToString())}  {entry.DurationMs} ms" +
            (string.IsNullOrEmpty(entry.Error) ? "" : "  " + entry.Error);

        private static string Details(ApiLogEntry entry)
        {
            var sb = new StringBuilder();
            sb.AppendLine(entry.Url);
            if (!string.IsNullOrEmpty(entry.RequestBody))
                sb.AppendLine("Request:").AppendLine(entry.RequestBody);
            if (!string.IsNullOrEmpty(entry.ResponseBody))
                sb.AppendLine("Response:").AppendLine(entry.ResponseBody);
            return sb.ToString().TrimEnd();
        }

        private void Clear()
        {
            foreach (var (_, view) in _entries)
                Destroy(view);
            _entries.Clear();
            _count.text = "";
        }

        private void CopyAll()
        {
            var sb = new StringBuilder();
            foreach (var (entry, _) in _entries)
                sb.AppendLine(Summary(entry)).AppendLine(Details(entry)).AppendLine();
            GUIUtility.systemCopyBuffer = sb.ToString();
            SetStatus($"Copied {_entries.Count} entries", Ui.Good);
        }
    }
}
