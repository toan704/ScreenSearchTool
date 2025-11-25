using Gma.System.MouseKeyHook;
using Microsoft.Web.WebView2.Core;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Local;
using Sdcb.PaddleOCR.Models.LocalV5;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;

namespace ScreenSearchTool
{
    public partial class Form1 : Form
    {
        private IKeyboardMouseEvents _hook;
        private Rectangle sel;
        private bool dragging;
        private Point start;
        private string ocrText = "";
        private PaddleOcrAll _paddleOcr;
        private Tesseract.TesseractEngine _tesseractEngine;
        private Dictionary<string, PaddleOcrAll> _paddleModels = new Dictionary<string, PaddleOcrAll>();
        private bool _useTesseract = false;
        private string _currentLang = "en";
        private readonly object _tesseractLock = new object();
        private List<string> _ocrHistory { get => SettingsManager.Load().OcrHistory ?? new List<string>(); set { var s = SettingsManager.Load(); s.OcrHistory = value; SettingsManager.Save(s); } }
        private Form _historyForm;

        public Form1()
        {
            InitializeComponent();
            KeyPreview = true;
            // Load & lưu kích cỡ form
            var settings = SettingsManager.Load();
            (Width, Height) = (settings.FormWidth, settings.FormHeight);
            Resize += (_, __) => { if (WindowState == FormWindowState.Normal) { settings.FormWidth = Width; settings.FormHeight = Height; SettingsManager.Save(settings); } };
            // Khởi tạo hook global
            _hook = Hook.GlobalEvents();
            _hook.KeyDown += (_, e) =>
            {
                if (e.Shift)
                {
                    if (e.KeyCode == Keys.D)
                    {
                        e.Handled = true;
                        this.Invoke((MethodInvoker)(() =>
                        {
                            var settings = SettingsManager.Load();
                            if (settings.MinimizeToTray)
                            {
                                if (Visible)
                                {
                                    Hide();
                                }
                                else
                                {
                                    Show();
                                    WindowState = FormWindowState.Normal;
                                    TopMost = true;
                                    Activate();
                                    BringToFront();
                                }
                            }
                        }));
                    }
                    else if (e.KeyCode == Keys.F)
                    {
                        e.Handled = true;
                        this.Invoke((MethodInvoker)(() =>
                        {
                            if (btnSelect.Enabled)
                                btnSelect_Click(null, EventArgs.Empty);
                        }));
                    }
                    else if (e.KeyCode == Keys.H)
                    {
                        e.Handled = true;
                        this.Invoke((MethodInvoker)(() =>
                        {
                            // Kiểm tra nếu form history đang hiện thì đóng
                            if (_historyForm != null && !_historyForm.IsDisposed && _historyForm.Visible)
                            {
                                _historyForm.Close();
                                _historyForm = null;
                            }
                            else
                            {
                                // Tạo form mới nếu chưa có hoặc đã dispose
                                btnListCache_Click(null, EventArgs.Empty);
                            }
                        }));
                    }
                }
            };


            TopMost = true;
            lblStatus.Text = "Đang khởi tạo...";
            Hide();
            _ = InitializeWebView2Async();
            _ = InitializeOCRAsync();
        }

        // 1. DANH SÁCH NGÔN NGỮ VỚI TÙY CHỌN TỰ ĐỘNG
        private static readonly (string Code, string Name, bool UseTesseract)[] Languages = new (string, string, bool)[]
        {
            ("auto", "Tự động phát hiện (Beta)", true),
            ("en", "Tiếng Anh", false),
            ("vi", "Tiếng Việt", true),
            ("zh-CN", "Tiếng Trung (Giản thể)", false),
            ("zh-TW", "Tiếng Trung (Phồn thể)", false),
            ("ja", "Tiếng Nhật", false),
            ("ko", "Tiếng Hàn", false),
            ("ar", "Tiếng Ả Rập", false),
            ("ru", "Tiếng Nga (Chữ Kirin)", false),
            ("hi", "Tiếng Hindi (Chữ Devanagari)", false),
            ("kn", "Tiếng Kannada", false),
            ("ta", "Tiếng Tamil", false),
            ("te", "Tiếng Telugu", false),
            ("la", "Tiếng Latinh", false)
        };

        // 2. DANH SÁCH NGÔN NGỮ TESSERACT HỖ TRỢ
        private static readonly string[] SupportedTessdataLanguages = new[]
        {
            "eng", "vie", "chi_sim", "chi_tra", "jpn", "kor", "fra", "deu", "spa", "por", "rus", "ara", "hin", "tha"
        };

        // 3. HÀM ĐẢM BẢO THƯ MỤC TESSDATA VÀ TẢI FILE THIẾU
        private string EnsureTessdataFolder()
        {
            string tessdataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
            if (!Directory.Exists(tessdataPath))
                Directory.CreateDirectory(tessdataPath);

            // Tải các file thiếu đồng thời
            var tasks = new List<Task>();
            foreach (var lang in SupportedTessdataLanguages)
            {
                string file = Path.Combine(tessdataPath, $"{lang}.traineddata");
                if (!File.Exists(file))
                {
                    tasks.Add(DownloadTessdataAsync(file, lang));
                }
            }
            return tessdataPath;
        }

        private async Task DownloadTessdataAsync(string path, string lang)
        {
            string url = $"https://github.com/tesseract-ocr/tessdata/raw/main/{lang}.traineddata";
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var data = await client.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(path, data);
            }
            catch { }
        }

        private async Task InitializeOCRAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    var settings = SettingsManager.Load();
                    var lang = settings.OcrLanguage ?? "en";
                    _currentLang = lang;

                    var selectedLang = Languages.FirstOrDefault(l => l.Code == lang);
                    _useTesseract = selectedLang.UseTesseract;

                    Invoke((MethodInvoker)(() =>
                    {
                        lblStatus.Text = "Khởi tạo OCR...";
                        lblStatus.ForeColor = Color.Orange;
                    }));
                    string tessdataPath = EnsureTessdataFolder();
                    string tessLang = GetTesseractLanguageString(lang);
                    _tesseractEngine = new Tesseract.TesseractEngine(tessdataPath, tessLang, Tesseract.EngineMode.Default);

                    if (!_useTesseract)
                    {
                        Invoke((MethodInvoker)(() =>
                        {
                            lblStatus.Text = $"{selectedLang.Name}";
                            lblStatus.ForeColor = Color.Orange;
                        }));
                        _paddleOcr = GetOrCreatePaddleModel(lang == "auto" ? "en" : lang);
                    }
                    else
                    {
                        Invoke((MethodInvoker)(() =>
                        {
                            lblStatus.Text = selectedLang.Name;
                            lblStatus.ForeColor = Color.Green;
                        }));
                    }
                }
                catch (Exception ex)
                {
                    Invoke((MethodInvoker)(() =>
                    {
                        lblStatus.Text = $"Khởi tạo lần đầu...";
                    }));
                }
            });
        }
        private string GetTesseractLanguageString(string langCode)
        {
            return langCode switch
            {
                "auto" => string.Join("+", GetAvailableTessdataLanguages()),
                "vi" => "vie+eng",
                _ => "eng"
            };
        }
        private string[] GetAvailableTessdataLanguages()
        {
            string tessdataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
            var availableLangs = new List<string>();

            foreach (var lang in SupportedTessdataLanguages)
            {
                string file = Path.Combine(tessdataPath, $"{lang}.traineddata");
                if (File.Exists(file))
                {
                    availableLangs.Add(lang);
                }
            }

            return availableLangs.Count > 0 ? availableLangs.ToArray() : new[] { "eng" };
        }
        private async void SwitchOCRLanguage(string newLangCode)
        {
            await Task.Run(() =>
            {
                try
                {
                    var selectedLang = Languages.FirstOrDefault(l => l.Code == newLangCode);
                    if (selectedLang.Code == null) return;

                    _currentLang = newLangCode;
                    _useTesseract = selectedLang.UseTesseract;
                    lock (_tesseractLock)
                    {
                        _tesseractEngine?.Dispose();
                        _tesseractEngine = null;
                    }

                    if (_paddleOcr != null && !_paddleModels.ContainsValue(_paddleOcr))
                    {
                        _paddleOcr.Dispose();
                        _paddleOcr = null;
                    }

                    // Khởi tạo lại engine mới
                    string tessdataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
                    string tessLang = GetTesseractLanguageString(newLangCode);

                    if (_useTesseract)
                    {
                        _tesseractEngine = new Tesseract.TesseractEngine(tessdataPath, tessLang, Tesseract.EngineMode.Default);
                        // ConfigureTesseractEngine(_tesseractEngine);
                    }
                    else
                    {
                        _paddleOcr = GetOrCreatePaddleModel(newLangCode == "auto" ? "en" : newLangCode);
                    }

                    var engineName = _useTesseract ? "Tesseract" : "PaddleOCR";
                    Invoke((MethodInvoker)(() =>
                    {
                        lblStatus.Text = $"{selectedLang.Name} ({engineName})";
                        lblStatus.ForeColor = Color.Green;
                    }));
                }
                catch (Exception ex)
                {
                    Invoke((MethodInvoker)(() =>
                    {
                        lblStatus.Text = $"Lỗi chuyển ngôn ngữ: {ex.Message}";
                        lblStatus.ForeColor = Color.Red;
                    }));
                }
            });
        }

        // CÁC PHƯƠNG THỨC KHÁC GIỮ NGUYÊN...
        private PaddleOcrAll GetOrCreatePaddleModel(string langCode)
        {
            if (_paddleModels.ContainsKey(langCode))
            {
                return _paddleModels[langCode];
            }
            var langMap = new Dictionary<string, FullOcrModel>
            {
                ["en"] = LocalFullModels.EnglishV4,
                ["zh-CN"] = LocalFullModels.ChineseV3,
                ["zh-TW"] = LocalFullModels.TraditionalChineseV3,
                ["ja"] = LocalFullModels.JapanV4,
                ["ko"] = LocalFullModels.KoreanV4,
                ["ar"] = LocalFullModels.ArabicV4,
                ["ru"] = LocalFullModels.CyrillicV3,
                ["hi"] = LocalFullModels.DevanagariV4,
                ["kn"] = LocalFullModels.KannadaV4,
                ["ta"] = LocalFullModels.TamilV4,
                ["te"] = LocalFullModels.TeluguV4,
                ["la"] = LocalFullModels.LatinV3
            };

            var model = langMap.ContainsKey(langCode) ? langMap[langCode] : langMap["en"];
            var paddleModel = new PaddleOcrAll(model, PaddleDevice.Mkldnn())
            {
                AllowRotateDetection = true,
                Enable180Classification = false
            };

            _paddleModels[langCode] = paddleModel;
            return paddleModel;
        }

        private async Task InitializeWebView2Async()
        {
            try
            {
                lblStatus.Text = "Đang khởi tạo WebView2...";
                string cachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenSearchTool\\WebView2Cache");
                if (!Directory.Exists(cachePath)) Directory.CreateDirectory(cachePath);
                var env = await CoreWebView2Environment.CreateAsync(userDataFolder: cachePath);
                await webView21.EnsureCoreWebView2Async(env);

                webView21.NavigationCompleted += (s, e) =>
                {
                    if (e.IsSuccess)
                    {
                        lblStatus.Text = "✓ Sẵn sàng";
                        lblStatus.ForeColor = Color.Green;
                    }
                    else
                    {
                        lblStatus.Text = "⚠ Tải trang thất bại";
                        lblStatus.ForeColor = Color.Orange;
                    }
                };

                webView21.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "app", "config",
                    CoreWebView2HostResourceAccessKind.Allow);

                webView21.CoreWebView2.WebMessageReceived += (s, e) => { if (e.TryGetWebMessageAsString() == "copyText") { Clipboard.SetText(ocrText); this.Invoke((MethodInvoker)delegate { lblStatus.Text = "✓ Đã copy văn bản"; }); } };

                lblStatus.Text = "Đang tải ScreenSearch...";
                webView21.Source = new Uri("https://app/homepage.html");
            }
            catch { lblStatus.Text = "Lỗi khởi tạo "; }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopRealtimeDetection();
            _detectionTimer?.Dispose();
            _hook?.Dispose();
            if (e.CloseReason == CloseReason.UserClosing && SettingsManager.Load().MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
            }
        }

        private string ExtractGeminiResponse(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                       candidates.GetArrayLength() > 0 &&
                       candidates[0].TryGetProperty("content", out var content) &&
                       content.TryGetProperty("parts", out var parts) &&
                       parts.GetArrayLength() > 0 &&
                       parts[0].TryGetProperty("text", out var text) ? text.GetString() ?? "" : "";
            }
            catch { return ""; }
        }

        private void btnSettings_Click(object sender, EventArgs e)
        {
            var settings = SettingsManager.Load();
            using var f = new Form() { Text = "Cài đặt", Size = new Size(500, 450), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, TopMost = true };
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 9, Padding = new Padding(12), AutoSize = true };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            // Mode
            table.Controls.Add(new Label { Text = "Chức năng:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            var cbMode = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(8, 3, 3, 3) };
            cbMode.Items.AddRange(new[] { "Tìm kiếm", "Dịch", "Trích xuất văn bản" }); cbMode.SelectedItem = settings.Mode ?? "Tìm kiếm";
            table.Controls.Add(cbMode, 1, 0);

            // OCR Language - ĐÃ CÓ TÙY CHỌN "TỰ ĐỘNG PHÁT HIỆN"
            table.Controls.Add(new Label { Text = "Ngôn ngữ trích xuất:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            var cbOcrLang = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(8, 3, 3, 3) };
            cbOcrLang.Items.AddRange(Languages.Select(l => l.Name).ToArray());
            cbOcrLang.SelectedIndex = Math.Max(0, Array.FindIndex(Languages, x => x.Code == (settings.OcrLanguage ?? "en")));
            cbOcrLang.SelectedIndexChanged += (s, ev) =>
            {
                var selectedLang = Languages[cbOcrLang.SelectedIndex];
                settings.OcrLanguage = selectedLang.Code;
                SettingsManager.Save(settings);
                SwitchOCRLanguage(selectedLang.Code);
            };
            table.Controls.Add(cbOcrLang, 1, 1);

            // Translate To
            table.Controls.Add(new Label { Text = "Dịch sang:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
            var cbToLang = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(8, 3, 3, 3), DropDownHeight = 200 };
            var translateLangs = new[] { ("af", "Afrikaans"), ("sq", "Albanian"), ("am", "Amharic"), ("ar-SA", "Arabic (Saudi Arabia)"), ("ar", "Arabic"), ("hy", "Armenian"), ("az", "Azerbaijani"), ("eu", "Basque"), ("be", "Belarusian"), ("bn-IN", "Bengali (India)"), ("bn", "Bengali"), ("bs-Cyrl", "Bosnian (Cyrillic)"), ("bs", "Bosnian"), ("bg", "Bulgarian"), ("my", "Burmese"), ("ca", "Catalan"), ("zh-CN", "Chinese (China)"), ("zh-HK", "Chinese (Hong Kong)"), ("zh-Hans", "Chinese (Simplified)"), ("zh-TW", "Chinese (Taiwan)"), ("zh-Hant", "Chinese (Traditional)"), ("zh", "Chinese"), ("hr", "Croatian"), ("cs", "Czech"), ("da", "Danish"), ("nl-BE", "Dutch (Belgium)"), ("nl", "Dutch"), ("en-AU", "English (Australia)"), ("en-CA", "English (Canada)"), ("en-NZ", "English (New Zealand)"), ("en-PH", "English (Philippines)"), ("en-ZA", "English (South Africa)"), ("en-GB", "English (United Kingdom)"), ("en-US", "English (United States)"), ("en", "English"), ("et", "Estonian"), ("fil", "Filipino"), ("fi", "Finnish"), ("fr-CA", "French (Canada)"), ("fr-CH", "French (Switzerland)"), ("fr", "French"), ("fy", "Frisian"), ("gl", "Galician"), ("ka", "Georgian"), ("de", "German"), ("el", "Greek"), ("gn", "Guarani"), ("gu", "Gujarati"), ("ha", "Hausa"), ("he", "Hebrew"), ("iw", "Hebrew"), ("hi", "Hindi"), ("hu", "Hungarian"), ("is", "Icelandic"), ("ig", "Igbo"), ("id", "Indonesian"), ("ga", "Irish"), ("it", "Italian"), ("ja", "Japanese"), ("kn", "Kannada"), ("km", "Khmer"), ("ko", "Korean"), ("ky", "Kyrgyz"), ("lo", "Lao"), ("lv", "Latvian"), ("ln", "Lingala"), ("lt", "Lithuanian"), ("lb", "Luxembourgish"), ("mk", "Macedonian"), ("ms", "Malay"), ("ml", "Malayalam"), ("mt", "Maltese"), ("mr", "Marathi"), ("mn", "Mongolian"), ("ne", "Nepali"), ("nb", "Norwegian Bokmal"), ("no", "Norwegian"), ("or", "Odia"), ("fa", "Persian"), ("pl", "Polish"), ("pt-BR", "Portuguese (Brazil)"), ("pt-PT", "Portuguese (Portugal)"), ("pt", "Portuguese"), ("pa-PK", "Punjabi (Pakistan)"), ("pa", "Punjabi"), ("ro", "Romanian"), ("ru", "Russian"), ("gd", "Scots Gaelic"), ("sr", "Serbian"), ("sk", "Slovak"), ("sl", "Slovenian"), ("so", "Somali"), ("es-AR", "Spanish (Argentina)"), ("es-CL", "Spanish (Chile)"), ("es-CO", "Spanish (Colombia)"), ("es-CR", "Spanish (Costa Rica)"), ("es-EC", "Spanish (Ecuador)"), ("es-SV", "Spanish (El Salvador)"), ("es-GT", "Spanish (Guatemala)"), ("es-HT", "Spanish (Haiti)"), ("es-HN", "Spanish (Honduras)"), ("es-419", "Spanish (Latin America)"), ("es-MX", "Spanish (Mexico)"), ("es-NI", "Spanish (Nicaragua)"), ("es-PA", "Spanish (Panama)"), ("es-PY", "Spanish (Paraguay)"), ("es-PE", "Spanish (Peru)"), ("es-PR", "Spanish (Puerto Rico)"), ("es-ES", "Spanish (Spain)"), ("es-US", "Spanish (United States)"), ("es-UY", "Spanish (Uruguay)"), ("es-VE", "Spanish (Venezuela)"), ("es", "Spanish"), ("sw", "Swahili"), ("sv", "Swedish"), ("tl", "Tagalog"), ("tg", "Tajik"), ("ta", "Tamil"), ("te", "Telugu"), ("th", "Thai"), ("tr", "Turkish"), ("uk", "Ukrainian"), ("ur", "Urdu"), ("uz", "Uzbek"), ("vi", "Vietnamese"), ("cy", "Welsh"), ("zu", "Zulu") };
            cbToLang.Items.AddRange(translateLangs.Select(l => $"{l.Item2} ({l.Item1})").ToArray()); cbToLang.SelectedIndex = Math.Max(0, Array.FindIndex(translateLangs, x => string.Equals(x.Item1, settings.TranslateTo ?? "en", StringComparison.OrdinalIgnoreCase)));
            table.Controls.Add(cbToLang, 1, 2);

            // API Key
            table.Controls.Add(new Label { Text = "API Gemini:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
            var txtApi = new TextBox { Text = settings.ApiKey, Dock = DockStyle.Fill, Margin = new Padding(8, 3, 3, 3) };
            table.Controls.Add(txtApi, 1, 3);

            // Kích cỡ
            table.Controls.Add(new Label { Text = "Kích cỡ:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
            var pnl = new Panel { Dock = DockStyle.Fill, Height = 30 };
            var trackSize = new TrackBar { Minimum = 480, Maximum = 1600, Value = this.Width, TickFrequency = 80, Dock = DockStyle.Left, Width = 305 };
            trackSize.ValueChanged += (s, ev) => { int w = trackSize.Value; int h = (int)(w * 378.0 / 640.0); this.Size = new Size(w, h); settings.FormWidth = w; settings.FormHeight = h; SettingsManager.Save(settings); };
            pnl.Controls.Add(trackSize);
            table.Controls.Add(pnl, 1, 4);

            var chkCopy = new CheckBox { Text = "Tự động lưu văn bản vào bộ nhớ tạm", Checked = settings.AutoCopy, AutoSize = true, Anchor = AnchorStyles.Left };
            table.Controls.Add(chkCopy, 0, 5); table.SetColumnSpan(chkCopy, 2);
            var chkLayout = new CheckBox { Text = "Hiển thị theo bố cục trích xuất", Checked = settings.ShowLayout, AutoSize = true, Anchor = AnchorStyles.Left };
            table.Controls.Add(chkLayout, 0, 6); table.SetColumnSpan(chkLayout, 2);
            var chkTray = new CheckBox { Text = "Ẩn vào khay hệ thống khi đóng Shift+D", Checked = settings.MinimizeToTray, AutoSize = true, Anchor = AnchorStyles.Left };
            table.Controls.Add(chkTray, 0, 7); table.SetColumnSpan(chkTray, 2);
            var btnClose = new Button { Text = "Đóng", DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right, Size = new Size(80, 30) };
            table.Controls.Add(btnClose, 1, 8);
            var lnk = new LinkLabel { Text = "Liên hệ: toan704 (fb.com/toan704)", AutoSize = true, Anchor = AnchorStyles.Left }; lnk.LinkClicked += (s, e) => System.Diagnostics.Process.Start("explorer", "https://fb.com/toan704"); table.Controls.Add(lnk, 0, 8);
            void UpdateVisibility()
            {
                bool isTranslate = cbMode.SelectedItem?.ToString() == "Dịch"; bool isExtract = cbMode.SelectedItem?.ToString() == "Văn bản trích xuất";
                table.GetControlFromPosition(0, 2).Visible = isTranslate; table.GetControlFromPosition(1, 2).Visible = isTranslate; chkLayout.Visible = isExtract;
            }
            UpdateVisibility();
            cbMode.SelectedIndexChanged += (s, ev) => { settings.Mode = cbMode.SelectedItem?.ToString() ?? "Tìm kiếm"; UpdateVisibility(); SettingsManager.Save(settings); };
            cbToLang.SelectedIndexChanged += (s, ev) => { if (cbToLang.SelectedIndex >= 0) { settings.TranslateTo = translateLangs[cbToLang.SelectedIndex].Item1; SettingsManager.Save(settings); } };
            txtApi.TextChanged += (s, ev) => { settings.ApiKey = txtApi.Text.Trim(); SettingsManager.Save(settings); };
            chkCopy.CheckedChanged += (s, ev) => { settings.AutoCopy = chkCopy.Checked; SettingsManager.Save(settings); };
            chkLayout.CheckedChanged += (s, ev) => { settings.ShowLayout = chkLayout.Checked; SettingsManager.Save(settings); };
            chkTray.CheckedChanged += (s, ev) => { settings.MinimizeToTray = chkTray.Checked; SettingsManager.Save(settings); };

            f.Controls.Add(table); f.ShowDialog();
        }

        // CÁC PHƯƠNG THỨC XỬ LÝ MODE VÀ OCR GIỮ NGUYÊN...
        private async Task HandleTranslateMode(string text, Settings settings)
        {
            lblStatus.Text = "Đang dịch...";
            string translated = await TranslateText(text, settings.TranslateTo ?? "en");
            webView21.NavigateToString($@"<!DOCTYPE html><html><head><meta charset='utf-8'><style>body{{margin:0;padding:12px 16px;font-family:'Segoe UI',Arial,sans-serif;background:#f9fafa;color:#1a1a1a;}}h5{{margin:0 0 10px;font-size:15px;color:#2563eb;font-weight:600;}}.result{{background:#e0f2fe;padding:14px;border-radius:12px;border-left:4px solid #0ea5e9;line-height:1.6;margin-bottom:14px;font-size:15px;}}.orig{{background:#f8f9fa;padding:12px;border-radius:10px;font-family:'Consolas','Courier New',monospace;font-size:13.5px;color:#4b5563;line-height:1.5;white-space:pre-wrap;word-wrap:break-word;}}hr{{border:none;border-top:1px solid #e2e8f0;margin:14px 0;}}small{{color:#64748b;font-size:12px;}}</style></head><body><h5>✨ Kết quả dịch</h5><div class='result'>{Html(translated)}</div><hr><small>Nguyên bản</small><div class='orig'>{Html(text)}</div></body></html>");
            lblStatus.Text = "✓ Dịch xong";
        }

        private void HandleExtractMode(string text, Settings settings) => webView21.NavigateToString($@"<!DOCTYPE html><html><head><meta charset='utf-8'><style>body{{margin:0;padding:12px 16px;font-family:'Segoe UI',Arial,sans-serif;background:#f9fafa;color:#1a1a1a;}}h5{{margin:0 0 10px;font-size:15px;color:#2563eb;font-weight:600;}}.result{{background:#e0f2fe;padding:14px;border-radius:12px;border-left:4px solid #0ea5e9;line-height:1.6;margin-bottom:14px;font-size:15px;white-space:pre-wrap;word-wrap:break-word;overflow-x:auto;}}.copy-btn{{background:#3b82f6;color:white;border:none;padding:6px 12px;border-radius:6px;cursor:pointer;font-size:12px;white-space:nowrap;flex-shrink:0;}}.copy-btn:hover{{background:#2563eb;}}</style></head><body><div style='display:flex;justify-content:space-between;align-items:center;margin-bottom:10px;gap:10px;'><h5>Văn bản trích xuất</h5><button class='copy-btn' onclick='window.chrome.webview.postMessage(""copyText"")'>Copy</button></div><div class='result'>{Html(settings.ShowLayout ? text.Replace("  ", " &nbsp;").Replace("\t", "&nbsp;&nbsp;&nbsp;&nbsp;") : text.Trim())}</div></body></html>");

        private void HandleSearchMode(string text) { webView21.Source = new Uri("https://www.google.com/search?q=" + Uri.EscapeDataString(text)); lblStatus.Text = "✓ Đang tìm kiếm..."; }

        private string Html(string s) => System.Net.WebUtility.HtmlEncode(s).Replace("\n", "<br>");
        private Mat BitmapToMat(Bitmap bitmap) { using var ms = new MemoryStream(); bitmap.Save(ms, ImageFormat.Png); return Cv2.ImDecode(ms.ToArray(), ImreadModes.Color); }
        private async Task<string> TranslateText(string text, string to, string from = "auto")
        {
            try
            {
                if (text.Length > 5000) return $"[Lỗi: Chỉ hỗ trợ tối đa 5000 ký tự. Tìm thấy {text.Length} ký tự.]";

                using var client = new WebClient();
                client.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                string response = await client.DownloadStringTaskAsync($"https://translate.google.com/m?tl={to}&sl={from}&q={Uri.EscapeDataString(text)}");

                var match = Regex.Match(response, @"class=""(?:t0|result-container)"">(.*?)<", RegexOptions.Singleline);
                return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : "[Không tìm thấy kết quả dịch]";
            }
            catch (Exception ex) { return $"[Lỗi dịch: {ex.Message}]"; }
        }

        private bool _isSelecting = false;
        private Form _overlayForm = null; // Thêm biến để lưu form overlay

        private void btnSelect_Click(object sender, EventArgs e)
        {
            // Nếu đang chọn vùng thì hủy
            if (_isSelecting && _overlayForm != null)
            {
                _overlayForm.Close();
                return;
            }

            _isSelecting = true;
            btnSelect.Text = "Shift+F";

            try
            {
                _overlayForm = new Form
                {
                    FormBorderStyle = 0,
                    Cursor = Cursors.Cross,
                    Opacity = 0.3,
                    BackColor = Color.Black,
                    WindowState = FormWindowState.Maximized,
                    TopMost = true
                };

                _overlayForm.MouseDown += (s, e2) => { dragging = true; start = e2.Location; };
                _overlayForm.MouseMove += (s, e2) => {
                    if (dragging)
                    {
                        sel = MakeRect(start, e2.Location);
                        _overlayForm.Invalidate();
                    }
                };

                _overlayForm.MouseUp += (s, e2) => {
                    dragging = false;
                    _overlayForm.Close();
                    if (sel.Width >= 5 && sel.Height >= 5)
                    {
                        btnUsingCrop.Enabled = true;
                        RunOCRFromRect(sel);
                    }
                    else
                    {
                        lblStatus.Text = "Vùng chọn quá nhỏ";
                    }
                };

                _overlayForm.Paint += (s, e2) => {
                    if (sel.Width > 0)
                        using (var pen = new Pen(Color.DeepSkyBlue, 3))
                            e2.Graphics.DrawRectangle(pen, sel);
                };

                _overlayForm.FormClosed += (s, e) => {
                    // Reset khi form đóng
                    _isSelecting = false;
                    _overlayForm = null;
                    btnSelect.Text = "Chọn vùng";
                    lblStatus.Text = "Đã hủy chọn vùng";
                };

                _overlayForm.ShowDialog();
            }
            catch (Exception ex)
            {
                _isSelecting = false;
                _overlayForm = null;
                btnSelect.Text = "Chọn vùng";
                lblStatus.Text = $"Lỗi: {ex.Message}";
            }
        }

        private void SetUIState(bool enabled, string status = null)
        {
            btnSelect.Enabled = enabled;
            btnUsingCrop.Enabled = enabled && sel.Width >= 5;
            btnAI.Enabled = enabled && !string.IsNullOrWhiteSpace(ocrText);
            Cursor = enabled ? Cursors.Default : Cursors.WaitCursor;
            if (status != null) lblStatus.Text = status;
        }
        private async void btnUsingCrop_Click(object sender, EventArgs e)
        {
            if (sel.Width < 5) { lblStatus.Text = "Chưa có vùng!"; lblStatus.ForeColor = Color.Orange; return; }
            using var overlay = new Form { FormBorderStyle = 0, BackColor = Color.Black, Opacity = 0.3, WindowState = FormWindowState.Maximized, TopMost = true };
            overlay.Paint += (s, e2) => e2.Graphics.DrawRectangle(new Pen(new[] { Color.Red, Color.Yellow, Color.Green, Color.Blue }[DateTime.Now.Millisecond / 250], 6), sel);
            overlay.Show();
            for (int i = 0; i < 10; i++) { overlay.Invalidate(); await Task.Delay(100); }
            overlay.Close();
            RunOCRFromRect(sel);
        }
        private async void RunOCRFromRect(Rectangle area)
        {
            if ((_paddleOcr == null && !_useTesseract) || (_tesseractEngine == null && _useTesseract))
            {
                lblStatus.Text = "...";
                lblStatus.ForeColor = Color.Red;
                return;
            }
            try
            {
                SetUIState(false, "Đang trích xuất...");
                using var bmp = new Bitmap(area.Width, area.Height);
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);

                ocrText = await Task.Run(() =>
                {
                    if (_useTesseract)
                    {
                        lock (_tesseractLock)
                        {
                            using var pix = Tesseract.PixConverter.ToPix(bmp);
                            using var page = _tesseractEngine.Process(pix, Tesseract.PageSegMode.Auto);
                            return page.GetText().Trim();
                        }
                    }
                    else
                    {
                        using var mat = BitmapToMat(bmp);
                        var result = _paddleOcr.Run(mat);
                        return result?.Text?.Trim() ?? "";
                    }
                });

                if (!string.IsNullOrWhiteSpace(ocrText))
                {
                    var history = _ocrHistory;
                    history.Add(ocrText);

                    if (history.Count > 100)
                        history.RemoveAt(0);

                    _ocrHistory = history;

                    var settings = SettingsManager.Load();
                    if (settings.AutoCopy) Clipboard.SetText(ocrText);
                    if (settings.Mode == "Dịch") await HandleTranslateMode(ocrText, settings);
                    else if (settings.Mode == "Trích xuất văn bản") HandleExtractMode(ocrText, settings);
                    else HandleSearchMode(ocrText);
                }
                else { lblStatus.Text = "Không thể trích xuất"; lblStatus.ForeColor = Color.Orange; }
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"Lỗi: {ex.Message}";
                lblStatus.ForeColor = Color.Red;
            }
            finally { SetUIState(true); }
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Shift && e.KeyCode == Keys.H)
            {
                e.Handled = true;
                btnListCache_Click(null, EventArgs.Empty);
            }
            else if (e.Shift && e.KeyCode == Keys.F)
            {
                e.Handled = true;
                btnSelect_Click(null, EventArgs.Empty);
            }
        }

        private async void btnAI_Click(object sender, EventArgs e)
        {
            var settings = SettingsManager.Load();
            if (string.IsNullOrWhiteSpace(settings.ApiKey)) { lblStatus.Text = "⚠ Chưa thiết lập API Key"; lblStatus.ForeColor = Color.Orange; return; }
            if (string.IsNullOrWhiteSpace(ocrText)) { webView21.NavigateToString("<html><body>Không có văn bản để gửi đến AI.</body></html>"); return; }

            SetUIState(false, "Đang xử lý AI...");
            try
            {
                using var client = new HttpClient();
                var response = await client.PostAsync($"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={settings.ApiKey}",
                    new StringContent(JsonSerializer.Serialize(new { contents = new[] { new { parts = new[] { new { text = ocrText } } } } }), System.Text.Encoding.UTF8, "application/json"));
                string aiText = response.IsSuccessStatusCode ? ExtractGeminiResponse(await response.Content.ReadAsStringAsync()) : "API Không hợp lệ!";
                webView21.NavigateToString($@"<!DOCTYPE html><html><head><meta charset='utf-8'><style>body{{margin:0;padding:12px 16px;font-family:'Segoe UI',Arial;background:#f9fafa;color:#1a1a1a}}h5{{margin:0 0 10px;font-size:15px;color:#2563eb;font-weight:600}}.result{{background:#e0f2fe;padding:14px;border-radius:12px;border-left:4px solid #0ea5e9;line-height:1.6;margin-bottom:14px;font-size:15px}}.orig{{background:#f8f9fa;padding:12px;border-radius:10px;font-family:Consolas,monospace;font-size:13.5px;color:#4b5563;line-height:1.5;white-space:pre-wrap}}hr{{border:none;border-top:1px solid #e2e8f0;margin:14px 0}}small{{color:#64748b;font-size:12px}}</style></head><body><h5>✨ Kết quả AI</h5><div class='result'>{Html(aiText)}</div><hr><small>Văn bản gốc</small><div class='orig'>{Html(ocrText)}</div></body></html>");
                lblStatus.Text = "✓ AI xử lý xong"; lblStatus.ForeColor = Color.Green;
            }
            catch { lblStatus.Text = "Lỗi AI"; lblStatus.ForeColor = Color.Red; }
            finally { SetUIState(true); }
        }


        private Rectangle MakeRect(Point p1, Point p2) => new Rectangle(Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y), Math.Abs(p1.X - p2.X), Math.Abs(p1.Y - p2.Y));

        private void btnListCache_Click(object sender, EventArgs e)
        {
            // Đóng form cũ nếu đang mở
            if (_historyForm != null && !_historyForm.IsDisposed)
            {
                _historyForm.Close();
                _historyForm = null;
            }

            var f = new Form
            {
                Text = "Lịch sử bản ghi (100 gần nhất) Shift+H",
                Size = new Size(420, 600),
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                TopMost = true
            };

            // Lưu reference
            _historyForm = f;

            // Xóa reference khi form đóng
            f.FormClosed += (s, ev) =>
            {
                if (_historyForm == f)
                    _historyForm = null;
            };

            var main = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(10) };
            main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            main.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var header = new Panel { Dock = DockStyle.Fill, Height = 40 };
            var btnClear = new Button { Text = "Xóa tất cả", Size = new Size(100, 30), Location = new Point(0, 5), BackColor = Color.FromArgb(239, 68, 68), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnClear.FlatAppearance.BorderSize = 0;
            var lblCount = new Label { Text = $"Tổng: {_ocrHistory.Count}", Location = new Point(110, 10), AutoSize = true, Font = new Font("Segoe UI", 10), ForeColor = Color.FromArgb(100, 116, 139) };
            header.Controls.AddRange(new Control[] { btnClear, lblCount });

            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                GridLines = false,
                HeaderStyle = ColumnHeaderStyle.None,
                Scrollable = true,
                HideSelection = false,
                Columns = { "", "" }
            };

            const int lineHeight = 16;
            listView.SmallImageList = new ImageList();
            listView.SmallImageList.ImageSize = new Size(1, lineHeight * 2 + 4);

            var status = new Label { Dock = DockStyle.Fill, Text = "", TextAlign = ContentAlignment.MiddleCenter, Height = 35, Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = Color.Green, Visible = false };

            Action<string, Color> showStatus = (msg, col) =>
            {
                status.Text = msg;
                status.ForeColor = col;
                status.Visible = true;
                var t = new System.Windows.Forms.Timer { Interval = 2000 };
                t.Tick += (s, ev) => { status.Visible = false; t.Stop(); t.Dispose(); };
                t.Start();
            };

            Action render = () =>
            {
                listView.BeginUpdate();
                listView.Items.Clear();
                var history = _ocrHistory;
                lblCount.Text = $"Tổng: {history.Count}";

                foreach (var txt in history.AsEnumerable().Reverse().Where(t => !string.IsNullOrWhiteSpace(t)))
                {
                    var item = new ListViewItem(GetFirstTwoLines(txt.Trim()));
                    item.SubItems.Add("📋 Copy");
                    item.Tag = txt.Trim();
                    listView.Items.Add(item);
                }

                listView.Columns[0].Width = listView.ClientSize.Width - 90;
                listView.Columns[1].Width = 85;

                listView.EndUpdate();
            };

            string GetFirstTwoLines(string text)
            {
                var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 0) return "";
                if (lines.Length == 1) return lines[0].Length > 100 ? lines[0].Substring(0, 100) + "..." : lines[0];

                var line1 = lines[0].Length > 80 ? lines[0].Substring(0, 80) + "..." : lines[0];
                var line2 = lines[1].Length > 80 ? lines[1].Substring(0, 80) + "..." : lines[1];
                return line1 + "\n" + line2;
            }

            listView.MouseClick += (s, ev) =>
            {
                var hit = listView.HitTest(ev.Location);
                if (hit.Item != null && hit.SubItem != null && hit.SubItem.Text == "📋 Copy")
                {
                    Clipboard.SetText(hit.Item.Tag?.ToString() ?? "");
                    showStatus("✓ Đã copy", Color.FromArgb(34, 197, 94));
                }
            };

            listView.DoubleClick += (s, ev) =>
            {
                if (listView.SelectedItems.Count > 0)
                {
                    Clipboard.SetText(listView.SelectedItems[0].Tag?.ToString() ?? "");
                    showStatus("✓ Đã copy", Color.FromArgb(34, 197, 94));
                }
            };

            listView.Resize += (s, ev) =>
            {
                listView.Columns[0].Width = listView.ClientSize.Width - 90;
                listView.Columns[1].Width = 85;
            };

            btnClear.Click += (s, ev) =>
            {
                _ocrHistory = new List<string>();
                render();
                showStatus("🗑️ Đã xóa", Color.FromArgb(239, 68, 68));
            };

            render();
            main.Controls.Add(header, 0, 0);
            main.Controls.Add(listView, 0, 1);
            main.Controls.Add(status, 0, 2);
            f.Controls.Add(main);
            f.Show(); // Đổi từ ShowDialog() sang Show() để không block
        }

        private System.Windows.Forms.Timer _detectionTimer;
        private bool _isDetecting = false;
        private string _lastDetectedText = "";
        private Bitmap _lastScreenshot = null;
        private bool _isProcessing = false;
        private DetectionOverlay _detectOverlay; // Biến lưu khung hiển thị

        private void InitializeDetectionTimer()
        {
            _detectionTimer = new System.Windows.Forms.Timer();
            _detectionTimer.Interval = 300; // Kiểm tra nhanh hơn nhưng chỉ OCR khi cần
            _detectionTimer.Tick += DetectionTimer_Tick;
        }

        // Triển khai nút Detect:
        private void btnDetect_Click(object sender, EventArgs e)
        {
            if (sel.Width < 5 || sel.Height < 5)
            {
                lblStatus.Text = "⚠ Chưa chọn vùng!";
                lblStatus.ForeColor = Color.Orange;
                return;
            }

            if (!_isDetecting)
            {
                StartRealtimeDetection();
            }
            else
            {
                StopRealtimeDetection();
            }
        }

        private void StartRealtimeDetection()
        {
            _isDetecting = true;
            _lastDetectedText = "";
            _lastScreenshot?.Dispose();
            _lastScreenshot = null;
            _isProcessing = false;

            // UI Update
            btnDetect.Text = "⏹";
            btnDetect.BackColor = Color.FromArgb(239, 68, 68);
            lblStatus.Text = "🔴 Đang theo dõi...";
            lblStatus.ForeColor = Color.Red;

            // --- HIỂN THỊ KHUNG OVERLAY ---
            if (_detectOverlay != null && !_detectOverlay.IsDisposed)
            {
                _detectOverlay.Close();
            }

            // Tạo và hiện khung bao quanh vùng 'sel'
            _detectOverlay = new DetectionOverlay(sel);
            _detectOverlay.Show();
            // -----------------------------

            // Bắt đầu detection timer
            if (_detectionTimer == null)
                InitializeDetectionTimer();

            _detectionTimer.Start();
        }

        private void StopRealtimeDetection()
        {
            _isDetecting = false;

            // UI Update
            btnDetect.Text = "Trực tiếp";
            btnDetect.BackColor = SystemColors.Control;
            lblStatus.Text = "✓ Đã dừng";
            lblStatus.ForeColor = Color.Green;

            _detectionTimer?.Stop();
            _lastScreenshot?.Dispose();
            _lastScreenshot = null;
            _lastDetectedText = "";

            // --- TẮT KHUNG OVERLAY ---
            if (_detectOverlay != null && !_detectOverlay.IsDisposed)
            {
                _detectOverlay.Close();
                _detectOverlay = null;
            }
            // -------------------------
        }

        private async void DetectionTimer_Tick(object sender, EventArgs e)
        {
            if (!_isDetecting || sel.Width < 5 || _isProcessing) return;

            try
            {
                using var bmp = new Bitmap(sel.Width, sel.Height);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(sel.Location, Point.Empty, sel.Size);
                }

                bool hasChanged = IsImageDifferent(bmp, _lastScreenshot);
                if (!hasChanged) return;

                _isProcessing = true;
                _detectionTimer.Stop();

                lblStatus.Text = "🔄 Đang xử lý...";
                lblStatus.ForeColor = Color.Orange;

                string text = await Task.Run(() =>
                {
                    if (_useTesseract)
                    {
                        lock (_tesseractLock)
                        {
                            using var pix = Tesseract.PixConverter.ToPix(bmp);
                            using var page = _tesseractEngine.Process(pix, Tesseract.PageSegMode.Auto);
                            return page.GetText().Trim();
                        }
                    }
                    else
                    {
                        using var mat = BitmapToMat(bmp);
                        var result = _paddleOcr.Run(mat);
                        return result?.Text?.Trim() ?? "";
                    }
                });

                _lastScreenshot?.Dispose();
                _lastScreenshot = new Bitmap(bmp);
                _lastDetectedText = text;

                var settings = SettingsManager.Load();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (settings.AutoCopy) Clipboard.SetText(text);

                    if (settings.Mode == "Dịch")
                        await HandleTranslateMode(text, settings);
                    else if (settings.Mode == "Trích xuất văn bản")
                        HandleExtractMode(text, settings);
                    else
                        HandleSearchMode(text);
                }
                else
                {
                    webView21.NavigateToString("<html><body style='padding:20px;font-family:Segoe UI'>⚠️ Không phát hiện text</body></html>");
                }

                lblStatus.Text = "✓ Đã phát hiện";
                lblStatus.ForeColor = Color.Green;

                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"❌ Lỗi: {ex.Message}";
                lblStatus.ForeColor = Color.Red;
            }
            finally
            {
                _isProcessing = false;
                if (_isDetecting && _detectionTimer != null)
                    _detectionTimer.Start();
            }
        }

        private bool IsImageDifferent(Bitmap current, Bitmap previous)
        {
            if (previous == null) return true;

            if (current.Width != previous.Width || current.Height != previous.Height)
                return true;

            BitmapData currentData = null;
            BitmapData previousData = null;

            try
            {
                currentData = current.LockBits(
                    new Rectangle(0, 0, current.Width, current.Height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format24bppRgb);

                previousData = previous.LockBits(
                    new Rectangle(0, 0, previous.Width, previous.Height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format24bppRgb);

                int bytes = Math.Abs(currentData.Stride) * current.Height;
                byte[] currentBytes = new byte[bytes];
                byte[] previousBytes = new byte[bytes];

                System.Runtime.InteropServices.Marshal.Copy(currentData.Scan0, currentBytes, 0, bytes);
                System.Runtime.InteropServices.Marshal.Copy(previousData.Scan0, previousBytes, 0, bytes);

                // So sánh với sampling thưa hơn và ngưỡng cao hơn
                int sampleCount = Math.Min(500, bytes / 3 / 200); // Chỉ lấy ~500 điểm mẫu
                int differentPixels = 0;
                int threshold = sampleCount / 10; // 10% sai khác (tăng từ 5%)

                var random = new Random();
                for (int i = 0; i < sampleCount; i++)
                {
                    int pos = random.Next(0, bytes - 3);

                    // So sánh với tolerance cao hơn (30 thay vì 15)
                    if (Math.Abs(currentBytes[pos] - previousBytes[pos]) > 30 ||
                        Math.Abs(currentBytes[pos + 1] - previousBytes[pos + 1]) > 30 ||
                        Math.Abs(currentBytes[pos + 2] - previousBytes[pos + 2]) > 30)
                    {
                        differentPixels++;
                        if (differentPixels > threshold)
                        {
                            return true; // Đủ khác biệt
                        }
                    }
                }

                return false; // Giống nhau
            }
            finally
            {
                if (currentData != null) current.UnlockBits(currentData);
                if (previousData != null) previous.UnlockBits(previousData);
            }
        }



    }

    class Settings
    {
        public bool AutoCopy { get; set; } = true;
        public string ApiKey { get; set; } = "";
        public string Mode { get; set; } = "Tìm kiếm";
        public string TranslateTo { get; set; } = "en";
        public string OcrLanguage { get; set; } = "en";
        public bool ShowLayout { get; set; } = false;
        public bool MinimizeToTray { get; set; } = true;
        public int FormWidth { get; set; } = 640;
        public int FormHeight { get; set; } = 378;
        public List<string> OcrHistory { get; set; } = new List<string>();
    }

    static class SettingsManager
    {
        static string file = "settings.json";
        public static Settings Load() => File.Exists(file) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(file)) : new Settings();
        public static void Save(Settings s) => File.WriteAllText(file, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
    }
}