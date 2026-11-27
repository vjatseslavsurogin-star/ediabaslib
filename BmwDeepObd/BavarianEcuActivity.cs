#if BAVARIAN_USB_BASELINE
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using EdiabasLib;

namespace BmwDeepObd
{
    // Private local files only. No transport is installed or opened in this activity.
    [Activity(Label = "ECU library", Exported = false,
        Name = ActivityCommon.AppNameSpace + "." + nameof(BavarianEcuActivity))]
    public class BavarianEcuActivity : AppCompatActivity
    {
        private const int ImportRequest = 510;
        private const long MaxFileBytes = 32L * 1024 * 1024;
        private const int MaxFiles = 128;
        private const long MaxLibraryBytes = 512L * 1024 * 1024;
        private static readonly Regex SafeName = new Regex(@"^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,100}\.(prg|grp)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private LinearLayout _body;
        private TextView _status;
        private TextView _jobs;
        private Spinner _files;
        private Button _import;
        private Button _inspect;
        private bool _busy;
        private bool _ru;
        private string LibraryPath => Path.Combine(FilesDir.AbsolutePath, "local-ecu");
        private string T(string en, string ru) => _ru ? ru : en;
        private int Dp(int value) => (int)(value * Resources.DisplayMetrics.Density + 0.5f);

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            _ru = BavarianLanguage.IsRussian(this);
            SupportActionBar?.Hide();
            var scroll = new ScrollView(this) { FillViewport = true };
            scroll.SetFitsSystemWindows(true);
            _body = new LinearLayout(this) { Orientation = Orientation.Vertical };
            _body.SetPadding(Dp(20), Dp(20), Dp(20), Dp(20));
            scroll.AddView(_body);
            SetContentView(scroll);
            AddText(T("ECU library · offline", "Библиотека ECU · офлайн"), 24);
            AddText(T("Import authorized EDIABAS PRG/GRP files from one matching data release. No files are sent to a server.",
                "Импортируйте разрешённые PRG/GRP из одной версии EDIABAS. Файлы не отправляются на сервер."), 15);
            _import = AddButton(T("Import PRG / GRP", "Импорт PRG / GRP"), (s,e) => PickFiles());
            _status = AddText("", 14);
            _files = new Spinner(this);
            _body.AddView(_files);
            _inspect = AddButton(T("List PRG jobs (offline)", "Список команд PRG (офлайн)"), async (s,e) => await InspectAsync());
            AddText(T("Only metadata is read. GRP identification and live ECU jobs are disabled here.",
                "Читаются только метаданные. Идентификация GRP и выполнение команд ECU здесь отключены."), 14);
            _jobs = AddText("", 13);
            _jobs.Text = BavarianEcuSeed.Initialize(this);
            RefreshFiles();
        }

        private TextView AddText(string text, int size)
        {
            var v = new TextView(this) { Text = text, TextSize = size };
            var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            lp.TopMargin = Dp(12);
            _body.AddView(v, lp);
            return v;
        }

        private Button AddButton(string label, EventHandler handler)
        {
            var b = new Button(this) { Text = label };
            b.Click += handler;
            _body.AddView(b);
            return b;
        }

        private string[] ListFiles()
        {
            Directory.CreateDirectory(LibraryPath);
            return Directory.EnumerateFiles(LibraryPath).Select(Path.GetFileName)
                .Where(n => n != null && SafeName.IsMatch(n)).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        }

        private void RefreshFiles()
        {
            var names = ListFiles();
            var prgs = names.Where(n => n.EndsWith(".prg", StringComparison.OrdinalIgnoreCase)).ToArray();
            var adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem, prgs);
            adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
            _files.Adapter = adapter;
            _inspect.Enabled = !_busy && prgs.Length > 0;
            _import.Enabled = !_busy;
            _status.Text = T("Local files: ", "Локальных файлов: ") + names.Length +
                " (PRG: " + prgs.Length + ", GRP: " + (names.Length - prgs.Length) + ")";
        }

        private void PickFiles()
        {
            var intent = new Intent(Intent.ActionOpenDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("*/*");
            intent.PutExtra(Intent.ExtraAllowMultiple, true);
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);
            try { StartActivityForResult(intent, ImportRequest); }
            catch (Exception ex) { _jobs.Text = BavarianLanguage.Error("ECU operation failed", ex); }
        }

        protected override async void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            if (requestCode != ImportRequest || resultCode != Result.Ok || data == null || _busy) return;
            var uris = new List<Android.Net.Uri>();
            if (data.ClipData != null)
            {
                for (int i = 0; i < Math.Min(data.ClipData.ItemCount, MaxFiles); i++)
                {
                    var uri = data.ClipData.GetItemAt(i)?.Uri;
                    if (uri != null) uris.Add(uri);
                }
            }
            else if (data.Data != null) uris.Add(data.Data);
            _busy = true;
            _import.Enabled = _inspect.Enabled = false;
            _jobs.Text = T("Importing...", "Импорт...");
            try
            {
                string report = await Task.Run(() => ImportFiles(uris));
                if (!IsFinishing && !IsDestroyed) _jobs.Text = report;
            }
            catch (Exception ex) { if (!IsFinishing && !IsDestroyed) _jobs.Text = BavarianLanguage.Error("ECU operation failed", ex); }
            finally
            {
                _busy = false;
                if (!IsFinishing && !IsDestroyed) RefreshFiles();
            }
        }

        private string ImportFiles(IEnumerable<Android.Net.Uri> uris)
        {
            var lines = new List<string>();
            Directory.CreateDirectory(LibraryPath);
            foreach (var uri in uris)
            {
                string temp = null;
                string name = "";
                try
                {
                    if (uri.Scheme != "content") throw new IOException("Unsupported URI");
                    using (var cursor = ContentResolver.Query(uri, new[] { OpenableColumns.DisplayName }, null, null, null))
                    {
                        if (cursor != null && cursor.MoveToFirst()) name = cursor.GetString(0);
                    }
                    if (string.IsNullOrEmpty(name) || !SafeName.IsMatch(name)) throw new IOException("Not a supported PRG/GRP filename");
                    name = name.ToLowerInvariant();
                    string target = Path.Combine(LibraryPath, name);
                    if (File.Exists(target))
                    {
                        lines.Add(name + ": " + "already exists; skipped");
                        continue;
                    }
                    if (ListFiles().Length >= MaxFiles) throw new IOException("Library file limit reached");
                    long existingBytes = Directory.EnumerateFiles(LibraryPath).Sum(f => new FileInfo(f).Length);
                    if (existingBytes >= MaxLibraryBytes) throw new IOException("Library size limit reached");
                    temp = Path.Combine(LibraryPath, Guid.NewGuid().ToString("N") + ".tmp");
                    long total = 0;
                    using (var input = ContentResolver.OpenInputStream(uri))
                    using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
                    {
                        if (input == null) throw new IOException("Cannot open selected file");
                        byte[] buffer = new byte[65536];
                        int n;
                        while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            total += n;
                            if (total > MaxFileBytes || existingBytes + total > MaxLibraryBytes) throw new IOException("File or library too large");
                            output.Write(buffer, 0, n);
                        }
                    }
                    if (total < 20) throw new IOException("File too short");
                    // Header validation uses upstream parser, without any ECU transport.
                    using (var ediabas = new EdiabasNet())
                    {
                        // Parser expects an actual extension. Check in a private staging directory.
                        string stage = Path.Combine(LibraryPath, Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(stage);
                        try
                        {
                            string staged = Path.Combine(stage, name);
                            File.Move(temp, staged);
                            temp = null;
                            ediabas.SetConfigProperty("EcuPath", stage);
                            uint type = ediabas.GetFileType(staged);
                            if ((type == 0) != name.EndsWith(".grp", StringComparison.OrdinalIgnoreCase))
                                throw new IOException("Header type does not match extension");
                            File.Move(staged, target);
                        }
                        finally { Directory.Delete(stage, true); }
                    }
                    lines.Add(name + ": " + T("imported", "импортирован"));
                }
                catch (Exception ex) { lines.Add(name + ": " + BavarianLanguage.Error("Import failed", ex)); }
                finally { if (temp != null && File.Exists(temp)) File.Delete(temp); }
            }
            return string.Join("\n", lines);
        }

        private async Task InspectAsync()
        {
            if (_busy) return;
            string name = _files.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(name) || !SafeName.IsMatch(name) || !name.EndsWith(".prg", StringComparison.OrdinalIgnoreCase)) return;
            _busy = true;
            _inspect.Enabled = _import.Enabled = false;
            _jobs.Text = T("Reading metadata...", "Чтение метаданных...");
            try
            {
                string[] jobs = await Task.Run(() =>
                {
                    using var ediabas = new EdiabasNet();
                    ediabas.SetConfigProperty("EcuPath", LibraryPath);
                    if (ediabas.GetFileType(Path.Combine(LibraryPath, name)) == 0) throw new IOException("Group file is not supported offline");
                    ediabas.SgbdFileName = name;
                    ediabas.NoInitForVJobs = true;
                    ediabas.ArgString = "ALL";
                    ediabas.ResultsRequests = string.Empty;
                    // Virtual metadata job only; no adapter and no ECU job execution.
                    ediabas.ExecuteJob("_JOBS");
                    return (ediabas.ResultSets ?? new List<Dictionary<string, EdiabasNet.ResultData>>())
                        .Where(row => row.TryGetValue("JOBNAME", out var item) && item.OpData is string)
                        .Select(row => (string)row["JOBNAME"].OpData).Distinct().OrderBy(x => x).ToArray();
                });
                if (!IsFinishing && !IsDestroyed)
                    _jobs.Text = name + " · " + jobs.Length + "\n" + string.Join("\n", jobs.Take(2000));
            }
            catch (Exception ex) { if (!IsFinishing && !IsDestroyed) _jobs.Text = BavarianLanguage.Error("ECU operation failed", ex); }
            finally
            {
                _busy = false;
                if (!IsFinishing && !IsDestroyed) RefreshFiles();
            }
        }
    }
}
#endif
