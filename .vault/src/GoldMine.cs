using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("Gold Mine · 曜蚀阁")]
[assembly: AssemblyProduct("Gold Mine · 曜蚀阁")]
[assembly: AssemblyDescription("Gold Mine · 曜蚀阁 — 人机双栖本地知识库与工具军火库")]

namespace GoldMineApp
{
    public class ItemMeta
    {
        public string note { get; set; }
        public List<string> tags { get; set; }
        public string entry { get; set; }
        public string folderMode { get; set; } // "" (auto), "subdir" (expanded group), "bundle" (collapsed card)
        public string updatedAt { get; set; }

        public ItemMeta()
        {
            note = "";
            tags = new List<string>();
            entry = "";
            folderMode = "";
            updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }

    public class ExternalItem
    {
        public string id { get; set; }
        public string name { get; set; }
        public string externalPath { get; set; }
        public string category { get; set; }
        public string zone { get; set; } // "outer" or "inner"
        public string note { get; set; }
        public string entry { get; set; }
        public List<string> tags { get; set; }
        public string addedAt { get; set; }

        public ExternalItem()
        {
            id = Guid.NewGuid().ToString("N").Substring(0, 10);
            name = "";
            externalPath = "";
            category = "工具与插件";
            zone = "outer";
            note = "";
            entry = "";
            tags = new List<string>();
            addedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }

    public class LedgerData
    {
        public List<string> categoryOrder { get; set; }
        public Dictionary<string, ItemMeta> metaByKey { get; set; }
        public List<ExternalItem> externalItems { get; set; }

        public LedgerData()
        {
            categoryOrder = new List<string>();
            metaByKey = new Dictionary<string, ItemMeta>(StringComparer.OrdinalIgnoreCase);
            externalItems = new List<ExternalItem>();
        }
    }

    public class BookItemDto
    {
        public string id { get; set; }
        public string key { get; set; }
        public string name { get; set; }
        public string category { get; set; }
        public string subDir { get; set; }
        public string zone { get; set; } // "outer" or "inner"
        public bool isExternal { get; set; }
        public bool isDirectory { get; set; }
        public bool existsOnDisk { get; set; }
        public string fullPath { get; set; }
        public string relPath { get; set; }
        public string itemType { get; set; } // "text", "html", "media", "tool", "bundle", "other"
        public string ext { get; set; }
        public long size { get; set; }
        public string updatedAt { get; set; }
        public string note { get; set; }
        public List<string> tags { get; set; }
        public string entry { get; set; }
        public List<string> detectedEntries { get; set; }
        public int childCount { get; set; }
    }

    public class SubDirDto
    {
        public string key { get; set; }
        public string name { get; set; }
        public string category { get; set; }
        public string zone { get; set; }
        public string fullPath { get; set; }
        public string note { get; set; }
        public int itemCount { get; set; }
    }

    public class CategoryDto
    {
        public string name { get; set; }
        public int outerCount { get; set; }
        public int innerCount { get; set; }
        public int totalCount { get; set; }
    }

    public class Program
    {
        public static string RootDir;
        public static string VaultDir;
        public static string LedgerPath;
        public static string UiPath;
        public static string OuterCatalogPath;
        public static string InnerCatalogPath;
        public static string IgnoreFilePath;
        public static int Port = 17890;
        public static string ServerUrl = "http://127.0.0.1:17890/";
        public static readonly string SessionToken = CreateCryptoToken();

        private class PreviewTicket
        {
            public string FilePath;
            public DateTime ExpiresAtUtc;
            public int RemainingUses;
        }

        private static readonly object TicketLock = new object();
        private static readonly Dictionary<string, PreviewTicket> PreviewTickets = new Dictionary<string, PreviewTicket>(StringComparer.Ordinal);

        private static string CreateCryptoToken()
        {
            byte[] buf = new byte[32];
            using (System.Security.Cryptography.RNGCryptoServiceProvider rng = new System.Security.Cryptography.RNGCryptoServiceProvider())
            {
                rng.GetBytes(buf);
            }
            StringBuilder sb = new StringBuilder(64);
            for (int i = 0; i < buf.Length; i++)
            {
                sb.Append(buf[i].ToString("x2"));
            }
            return sb.ToString();
        }

        private static string IssuePreviewTicket(string fullPath)
        {
            string ext = Path.GetExtension(fullPath).ToLowerInvariant();
            bool isAvStream = ext == ".mp4" || ext == ".webm" || ext == ".mov" || ext == ".mkv" ||
                              ext == ".mp3" || ext == ".wav" || ext == ".ogg" || ext == ".flac" || ext == ".m4a";
            string ticketId = CreateCryptoToken();
            DateTime now = DateTime.UtcNow;
            lock (TicketLock)
            {
                List<string> expired = new List<string>();
                foreach (KeyValuePair<string, PreviewTicket> kv in PreviewTickets)
                {
                    if (kv.Value == null || kv.Value.ExpiresAtUtc < now || kv.Value.RemainingUses <= 0)
                    {
                        expired.Add(kv.Key);
                    }
                }
                foreach (string k in expired) PreviewTickets.Remove(k);

                PreviewTicket pt = new PreviewTicket();
                pt.FilePath = fullPath;
                pt.ExpiresAtUtc = now.AddSeconds(60);
                // HTML/SVG/图像等严格即看即焚（1次作废）；音视频容许浏览器媒体管线在60秒内预读握手
                pt.RemainingUses = isAvStream ? 4 : 1;
                PreviewTickets[ticketId] = pt;
            }
            return ticketId;
        }

        private static string ConsumePreviewTicket(string ticketId)
        {
            if (string.IsNullOrEmpty(ticketId)) return null;
            DateTime now = DateTime.UtcNow;
            lock (TicketLock)
            {
                PreviewTicket pt;
                if (!PreviewTickets.TryGetValue(ticketId, out pt) || pt == null)
                {
                    return null;
                }
                if (pt.ExpiresAtUtc < now || pt.RemainingUses <= 0)
                {
                    PreviewTickets.Remove(ticketId);
                    return null;
                }
                pt.RemainingUses--;
                if (pt.RemainingUses <= 0)
                {
                    PreviewTickets.Remove(ticketId);
                }
                return pt.FilePath;
            }
        }

        private static readonly object SyncLock = new object();
        private static LedgerData Ledger = new LedgerData();
        private static HttpListener Listener;
        private static FileSystemWatcher OuterWatcher;
        private static System.Threading.Timer DebounceTimer;
        private static NotifyIcon TrayIcon;
        private static bool IsSyncing = false;
        private static int DialogPickBusy = 0;
        private static List<string> RecentLogs = new List<string>();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(
            IntPtr hFile, StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        private const uint FILE_SHARE_READ_WRITE_DELETE = 0x00000001 | 0x00000002 | 0x00000004;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= (a[i] ^ b[i]);
            return diff == 0;
        }

        private static bool IsReparsePointPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                if (!File.Exists(path) && !Directory.Exists(path)) return false;
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// 通过 Windows 内核句柄解析真实最终物理路径（穿透所有软链接/联接点/SUBST 映射），
        /// 若目标尚未创建则沿最近已存在父目录解析并拼接剩余段。
        /// </summary>
        private static string ResolveRealPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try
            {
                string full = Path.GetFullPath(path).TrimEnd('\\', '/');
                string probe = full;
                Stack<string> tail = new Stack<string>();
                while (!string.IsNullOrEmpty(probe) && !File.Exists(probe) && !Directory.Exists(probe))
                {
                    string parent = Path.GetDirectoryName(probe);
                    string leaf = Path.GetFileName(probe);
                    if (string.IsNullOrEmpty(parent) || string.Equals(parent, probe, StringComparison.OrdinalIgnoreCase))
                        return full;
                    tail.Push(leaf);
                    probe = parent.TrimEnd('\\', '/');
                }

                string resolved = probe;
                IntPtr h = CreateFileW(probe, 0, FILE_SHARE_READ_WRITE_DELETE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
                if (h != INVALID_HANDLE_VALUE && h != IntPtr.Zero)
                {
                    try
                    {
                        StringBuilder sb = new StringBuilder(1024);
                        uint len = GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0);
                        if (len > 0 && len < sb.Capacity)
                        {
                            string finalP = sb.ToString();
                            if (finalP.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                                finalP = @"\\" + finalP.Substring(8);
                            else if (finalP.StartsWith(@"\\?\", StringComparison.Ordinal))
                                finalP = finalP.Substring(4);
                            resolved = finalP.TrimEnd('\\', '/');
                        }
                    }
                    finally
                    {
                        CloseHandle(h);
                    }
                }

                while (tail.Count > 0)
                {
                    resolved = Path.Combine(resolved, tail.Pop());
                }
                return resolved.TrimEnd('\\', '/');
            }
            catch
            {
                return "";
            }
        }

        private static void WriteTextAtomic(string targetPath, string content, bool keepBackup)
        {
            string dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmpPath = targetPath + ".gm_tmp_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            try
            {
                File.WriteAllText(tmpPath, content, new UTF8Encoding(false));
                if (File.Exists(targetPath))
                {
                    string bakPath = keepBackup ? (targetPath + ".bak") : null;
                    try
                    {
                        File.Replace(tmpPath, targetPath, bakPath, true);
                    }
                    catch
                    {
                        File.Copy(tmpPath, targetPath, true);
                        try { File.Delete(tmpPath); } catch { }
                    }
                }
                else
                {
                    File.Move(tmpPath, targetPath);
                }
            }
            finally
            {
                if (File.Exists(tmpPath))
                {
                    try { File.Delete(tmpPath); } catch { }
                }
            }
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_SHOWWINDOW = 0x0040;

        public static void ForceWindowToForeground(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            try
            {
                IntPtr fg = GetForegroundWindow();
                uint fgPid;
                uint fgThread = GetWindowThreadProcessId(fg, out fgPid);
                uint curThread = GetCurrentThreadId();
                bool attached = false;

                if (fgThread != 0 && fgThread != curThread)
                {
                    attached = AttachThreadInput(curThread, fgThread, true);
                }

                keybd_event(0x12, 0, 0x0002, UIntPtr.Zero); // VK_MENU keyup

                if (IsIconic(hWnd))
                {
                    ShowWindow(hWnd, 9); // SW_RESTORE
                }
                else
                {
                    ShowWindow(hWnd, 5); // SW_SHOW
                }

                SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                SetForegroundWindow(hWnd);
                BringWindowToTop(hWnd);
                SetWindowPos(hWnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                SetForegroundWindow(hWnd);

                if (attached)
                {
                    AttachThreadInput(curThread, fgThread, false);
                }
            }
            catch { }
        }

        private static List<IntPtr> GetExplorerWindows()
        {
            List<IntPtr> list = new List<IntPtr>();
            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                if (!IsWindowVisible(hWnd)) return true;
                StringBuilder sbClass = new StringBuilder(128);
                GetClassName(hWnd, sbClass, sbClass.Capacity);
                string cls = sbClass.ToString();
                if (cls == "CabinetWClass" || cls == "ExploreWClass")
                {
                    list.Add(hWnd);
                }
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static void OpenExplorerAndPopToFront(string targetPath, bool selectFile)
        {
            if (string.IsNullOrEmpty(targetPath) || targetPath.IndexOf('"') >= 0) return;
            for (int c = 0; c < targetPath.Length; c++) if (targetPath[c] < 32) return;
            targetPath = Path.GetFullPath(targetPath);
            HashSet<IntPtr> before = new HashSet<IntPtr>(GetExplorerWindows());
            string folderPath = selectFile ? Path.GetDirectoryName(targetPath) : targetPath;
            string folderLeaf = Path.GetFileName((folderPath ?? targetPath).TrimEnd('\\', '/'));

            AllowSetForegroundWindow(-1);
            keybd_event(0x12, 0, 0x0002, UIntPtr.Zero);

            string args = selectFile ? ("/select,\"" + targetPath + "\"") : ("\"" + targetPath + "\"");
            Process.Start("explorer.exe", args);

            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                IntPtr foundNew = IntPtr.Zero;
                for (int i = 0; i < 20; i++)
                {
                    Thread.Sleep(55);
                    List<IntPtr> current = GetExplorerWindows();
                    foreach (IntPtr h in current)
                    {
                        if (!before.Contains(h))
                        {
                            foundNew = h;
                            break;
                        }
                    }
                    if (foundNew != IntPtr.Zero) break;
                }

                if (foundNew != IntPtr.Zero)
                {
                    ForceWindowToForeground(foundNew);
                    return;
                }

                // 如果复用了已打开的资源管理器窗口，匹配标题并强行拉到最前
                List<IntPtr> allAfter = GetExplorerWindows();
                foreach (IntPtr h in allAfter)
                {
                    StringBuilder sbTitle = new StringBuilder(256);
                    GetWindowText(h, sbTitle, sbTitle.Capacity);
                    string title = sbTitle.ToString();
                    if (!string.IsNullOrEmpty(folderLeaf) &&
                        title.IndexOf(folderLeaf, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        ForceWindowToForeground(h);
                        return;
                    }
                }

                if (allAfter.Count > 0)
                {
                    ForceWindowToForeground(allAfter[0]);
                }
            });
        }

        [STAThread]
        public static void Main(string[] args)
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            RootDir = exeDir;

            VaultDir = Path.Combine(RootDir, ".vault");
            LedgerPath = Path.Combine(VaultDir, ".goldmine.json");
            UiPath = Path.Combine(VaultDir, "ui", "index.html");
            OuterCatalogPath = Path.Combine(RootDir, "目录.md");
            InnerCatalogPath = Path.Combine(VaultDir, "目录_里.md");
            IgnoreFilePath = Path.Combine(RootDir, ".ignore");

            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Global\\GoldMine_TreasurePavilion_Mutex_v1", out createdNew))
            {
                bool silent = false;
                bool syncOnly = false;
                foreach (string a in args)
                {
                    if (string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase)) silent = true;
                    if (string.Equals(a, "--sync-only", StringComparison.OrdinalIgnoreCase)) syncOnly = true;
                }

                if (!createdNew)
                {
                    if (!syncOnly && !silent)
                    {
                        try
                        {
                            Process.Start(ServerUrl);
                        }
                        catch { }
                    }
                    return;
                }

                if (syncOnly)
                {
                    InitializeStorage();
                    SyncAndGenerateCatalogs();
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                InitializeStorage();
                SyncAndGenerateCatalogs();
                StartWatcher();
                StartHttpServer();
                SetupTrayIcon();

                if (!silent)
                {
                    try
                    {
                        Process.Start(ServerUrl);
                    }
                    catch { }
                }

                Application.Run();
            }
        }

        private static void LogEvent(string msg)
        {
            lock (RecentLogs)
            {
                string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg;
                RecentLogs.Insert(0, line);
                if (RecentLogs.Count > 50) RecentLogs.RemoveAt(RecentLogs.Count - 1);
            }
        }

        private static void InitializeStorage()
        {
            Directory.CreateDirectory(RootDir);
            Directory.CreateDirectory(VaultDir);
            try
            {
                DirectoryInfo vdi = new DirectoryInfo(VaultDir);
                if ((vdi.Attributes & FileAttributes.Hidden) == 0)
                {
                    vdi.Attributes |= FileAttributes.Hidden;
                }
            }
            catch { }
            Directory.CreateDirectory(Path.Combine(VaultDir, "ui"));

            EnsureIgnoreFile();
            LoadLedger();

            string[] defaultCats = new string[] { "角色与设定", "文本与产出", "工具与插件" };
            if (Ledger.categoryOrder.Count == 0)
            {
                foreach (string c in defaultCats)
                {
                    string outerCat = Path.Combine(RootDir, c);
                    string innerCat = Path.Combine(VaultDir, c);
                    Directory.CreateDirectory(outerCat);
                    Directory.CreateDirectory(innerCat);
                    if (!Ledger.categoryOrder.Contains(c)) Ledger.categoryOrder.Add(c);
                }
                SaveLedger();
            }
        }

        private static void EnsureIgnoreFile()
        {
            string requiredContent =
@"# Gold Mine 里书架与内部元数据隔离规则（防全局 grep/rg/find 误扫污染上下文）
.vault/
.vault/**
**/目录_里.md
目录_里.md
_*
**/_*
";
            try
            {
                if (!File.Exists(IgnoreFilePath))
                {
                    File.WriteAllText(IgnoreFilePath, requiredContent, new UTF8Encoding(false));
                }
                else
                {
                    string existing = File.ReadAllText(IgnoreFilePath, Encoding.UTF8);
                    if (!existing.Contains(".vault/") || !existing.Contains("目录_里.md") || !existing.Contains("_*"))
                    {
                        File.WriteAllText(IgnoreFilePath, requiredContent, new UTF8Encoding(false));
                    }
                }
            }
            catch { }
        }

        private static void LoadLedger()
        {
            lock (SyncLock)
            {
                try
                {
                    if (File.Exists(LedgerPath))
                    {
                        string json = File.ReadAllText(LedgerPath, Encoding.UTF8);
                        JavaScriptSerializer ser = new JavaScriptSerializer();
                        ser.MaxJsonLength = int.MaxValue;
                        LedgerData loaded = ser.Deserialize<LedgerData>(json);
                        if (loaded != null)
                        {
                            if (loaded.categoryOrder == null) loaded.categoryOrder = new List<string>();
                            if (loaded.metaByKey == null) loaded.metaByKey = new Dictionary<string, ItemMeta>(StringComparer.OrdinalIgnoreCase);
                            else loaded.metaByKey = new Dictionary<string, ItemMeta>(loaded.metaByKey, StringComparer.OrdinalIgnoreCase);
                            if (loaded.externalItems == null) loaded.externalItems = new List<ExternalItem>();
                            Ledger = loaded;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogEvent("读取账本异常: " + ex.Message);
                }
            }
        }

        private static void SaveLedger()
        {
            lock (SyncLock)
            {
                try
                {
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    ser.MaxJsonLength = int.MaxValue;
                    string json = ser.Serialize(Ledger);
                    WriteTextAtomic(LedgerPath, json, true);
                }
                catch (Exception ex)
                {
                    LogEvent("保存账本异常: " + ex.Message);
                }
            }
        }

        private static void MigrateLedgerKeyTree(string oldKey, string newKey, bool overwriteExisting)
        {
            if (string.IsNullOrEmpty(oldKey) || string.IsNullOrEmpty(newKey) ||
                string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            string oldPrefix = oldKey + "\\";
            string newPrefix = newKey + "\\";
            List<string> keys = new List<string>();
            foreach (string k in Ledger.metaByKey.Keys)
            {
                if (string.Equals(k, oldKey, StringComparison.OrdinalIgnoreCase) ||
                    k.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    keys.Add(k);
                }
            }
            foreach (string k in keys)
            {
                ItemMeta m = Ledger.metaByKey[k];
                Ledger.metaByKey.Remove(k);
                string nk = string.Equals(k, oldKey, StringComparison.OrdinalIgnoreCase)
                    ? newKey
                    : (newPrefix + k.Substring(oldPrefix.Length));
                if (overwriteExisting || !Ledger.metaByKey.ContainsKey(nk))
                {
                    Ledger.metaByKey[nk] = m;
                }
            }
        }

        /// <summary>
        /// 将源目录中的全部子项安全合并到已存在的目标目录，按实际落盘名精确迁移每项及其后代账本 key（不覆盖目标已有元信息、跳过软链接）
        /// </summary>
        private static void MergeDirectoryAndMigrateKeys(string srcDir, string dstDir, string srcParentKey, string dstParentKey, bool stripUnderscores)
        {
            Directory.CreateDirectory(dstDir);
            foreach (string entry in Directory.GetFileSystemEntries(srcDir))
            {
                if (IsReparsePointPath(entry)) continue;
                string origName = Path.GetFileName(entry);
                if (string.IsNullOrEmpty(origName)) continue;
                string desired = stripUnderscores ? StripLeadingUnderscores(origName, Directory.Exists(entry)) : origName;
                if (string.IsNullOrEmpty(desired)) desired = origName;
                bool ren;
                string moved = MovePhysicalEntrySafe(entry, dstDir, desired, out ren);
                string finalName = Path.GetFileName(moved);
                if (!string.IsNullOrEmpty(srcParentKey) && !string.IsNullOrEmpty(dstParentKey))
                {
                    MigrateLedgerKeyTree(srcParentKey + "\\" + origName, dstParentKey + "\\" + finalName, false);
                }
            }
            if (Directory.Exists(srcDir) && Directory.GetFileSystemEntries(srcDir).Length == 0)
            {
                Directory.Delete(srcDir, false);
            }
        }

        /// <summary>
        /// 同名文件/文件夹防覆盖保护：如果目标路径已存在，自动追加 (2), (3)... 绝不覆盖已有数据
        /// </summary>
        public static string GetCollisionSafePath(string targetDir, string desiredName, out bool wasRenamed)
        {
            wasRenamed = false;
            Directory.CreateDirectory(targetDir);
            string cleanName = desiredName.Trim();
            if (string.IsNullOrEmpty(cleanName)) cleanName = "未命名藏品";

            string candidatePath = Path.Combine(targetDir, cleanName);
            if (!File.Exists(candidatePath) && !Directory.Exists(candidatePath))
            {
                return candidatePath;
            }

            wasRenamed = true;
            string stem = Path.GetFileNameWithoutExtension(cleanName);
            string ext = Path.GetExtension(cleanName);
            if (string.IsNullOrEmpty(stem))
            {
                stem = cleanName;
                ext = "";
            }

            int counter = 2;
            while (true)
            {
                string nextName = string.Format("{0} ({1}){2}", stem, counter, ext);
                string nextPath = Path.Combine(targetDir, nextName);
                if (!File.Exists(nextPath) && !Directory.Exists(nextPath))
                {
                    return nextPath;
                }
                counter++;
            }
        }

        /// <summary>
        /// 安全搬移内部文件或文件夹（支持跨表里移动与同名防覆盖保护）
        /// </summary>
        public static string MovePhysicalEntrySafe(string sourcePath, string targetDir, string desiredName, out bool wasRenamed)
        {
            wasRenamed = false;
            if (string.IsNullOrEmpty(sourcePath) || (!File.Exists(sourcePath) && !Directory.Exists(sourcePath)))
            {
                throw new FileNotFoundException("源路径不存在", sourcePath);
            }
            if (IsReparsePointPath(sourcePath))
            {
                throw new UnauthorizedAccessException("安全护栏：拒绝搬移软链接或联接点");
            }
            Directory.CreateDirectory(targetDir);
            if (IsReparsePointPath(targetDir))
            {
                throw new UnauthorizedAccessException("安全护栏：目标目录为软链接或联接点");
            }

            string fullSource = Path.GetFullPath(sourcePath).TrimEnd('\\', '/');
            string fullTargetDir = Path.GetFullPath(targetDir).TrimEnd('\\', '/');
            if (Directory.Exists(sourcePath) &&
                (string.Equals(fullTargetDir, fullSource, StringComparison.OrdinalIgnoreCase) ||
                 fullTargetDir.StartsWith(fullSource + "\\", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("不能将目录移动到其自身或子目录内部");
            }

            string cleanDesired = SanitizeSingleSegmentName(desiredName);
            if (string.IsNullOrEmpty(cleanDesired) || IsWindowsReservedDeviceName(cleanDesired))
            {
                cleanDesired = "未命名藏品";
            }

            string directTarget = Path.GetFullPath(Path.Combine(targetDir, cleanDesired)).TrimEnd('\\', '/');
            if (string.Equals(fullSource, directTarget, StringComparison.OrdinalIgnoreCase))
            {
                return sourcePath;
            }

            string finalDest = GetCollisionSafePath(targetDir, cleanDesired, out wasRenamed);
            if (File.Exists(sourcePath))
            {
                File.Move(sourcePath, finalDest);
            }
            else if (Directory.Exists(sourcePath))
            {
                Directory.Move(sourcePath, finalDest);
            }
            return finalDest;
        }

        private static string StripLeadingUnderscores(string name)
        {
            return StripLeadingUnderscores(name, false);
        }

        private static string StripLeadingUnderscores(string name, bool isDirectory)
        {
            if (string.IsNullOrEmpty(name)) return name;
            string stripped = name.TrimStart('_').Trim();
            if (string.IsNullOrEmpty(stripped))
            {
                return isDirectory ? "" : "未命名藏品";
            }
            if (stripped.StartsWith("."))
            {
                // 若是 "_." 或 "_.md" 这类仅剩扩展名的文件，补上安全前缀避免保留 "_" 导致表->里无限回弹
                if (isDirectory) return "";
                string extOnly = SanitizeSingleSegmentName(stripped);
                if (string.IsNullOrEmpty(extOnly) || extOnly == ".") return "未命名藏品";
                return extOnly.StartsWith(".") ? ("未命名" + extOnly) : extOnly;
            }
            return stripped;
        }

        private static bool IsWindowsReservedDeviceName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string baseName = name.Trim().TrimEnd('.');
            int dot = baseName.IndexOf('.');
            if (dot >= 0) baseName = baseName.Substring(0, dot);
            baseName = baseName.Trim().ToUpperInvariant();
            if (baseName == "CON" || baseName == "PRN" || baseName == "AUX" || baseName == "NUL") return true;
            if (baseName.Length == 4 &&
                (baseName.StartsWith("COM") || baseName.StartsWith("LPT")) &&
                baseName[3] >= '1' && baseName[3] <= '9')
            {
                return true;
            }
            return false;
        }

        private static string SanitizeSingleSegmentName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            StringBuilder clean = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                if (name[i] >= 32) clean.Append(name[i]);
            }
            string s = clean.ToString().Trim();
            s = s.Replace("/", "").Replace("\\", "");
            while (s.Contains("..")) s = s.Replace("..", "");
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in invalid) s = s.Replace(c.ToString(), "");
            s = s.Trim().TrimEnd('.');
            if (s == "." || IsWindowsReservedDeviceName(s)) return "";
            return s;
        }

        private static bool IsReservedRootEntry(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            if (name.StartsWith(".")) return true; // .vault, .ignore, .git, etc.
            if (IsWindowsReservedDeviceName(name)) return true;
            string lower = name.ToLowerInvariant();
            if (lower == "目录.md" || lower == "目录_里.md" || lower == "使用说明.md" ||
                lower == "曜蚀阁.exe" || lower == "藏宝阁.exe" || lower == "打开曜蚀阁.bat" || lower == "打开藏宝阁.bat" ||
                lower == "ui" || lower == "src")
                return true;
            return false;
        }

        private static bool IsProtectedNormalizedPath(string full, bool allowCatalogAndUiAssetsRead)
        {
            if (string.IsNullOrEmpty(full)) return true;
            string exeFull = Path.GetFullPath(Assembly.GetExecutingAssembly().Location).TrimEnd('\\', '/');
            string defaultExeFull = Path.GetFullPath(Path.Combine(RootDir, "曜蚀阁.exe")).TrimEnd('\\', '/');
            string legacyExeFull = Path.GetFullPath(Path.Combine(RootDir, "藏宝阁.exe")).TrimEnd('\\', '/');
            string ledgerFull = Path.GetFullPath(LedgerPath).TrimEnd('\\', '/');
            string ledgerBakFull = ledgerFull + ".bak";
            string ignoreFull = Path.GetFullPath(IgnoreFilePath).TrimEnd('\\', '/');
            string srcDirFull = Path.GetFullPath(Path.Combine(VaultDir, "src")).TrimEnd('\\', '/');
            string uiDirFull = Path.GetFullPath(Path.Combine(VaultDir, "ui")).TrimEnd('\\', '/');
            string uiAssetsFull = Path.GetFullPath(Path.Combine(uiDirFull, "assets")).TrimEnd('\\', '/');
            string outerCatFull = Path.GetFullPath(OuterCatalogPath).TrimEnd('\\', '/');
            string innerCatFull = Path.GetFullPath(InnerCatalogPath).TrimEnd('\\', '/');
            string guideFull = Path.GetFullPath(Path.Combine(RootDir, "使用说明.md")).TrimEnd('\\', '/');

            if (string.Equals(full, exeFull, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(full, defaultExeFull, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(full, legacyExeFull, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(full, ledgerFull, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(full, ledgerBakFull, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(full, ignoreFull, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(full, srcDirFull, StringComparison.OrdinalIgnoreCase) ||
                full.StartsWith(srcDirFull + "\\", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(full, uiDirFull, StringComparison.OrdinalIgnoreCase) ||
                full.StartsWith(uiDirFull + "\\", StringComparison.OrdinalIgnoreCase))
            {
                if (allowCatalogAndUiAssetsRead && full.StartsWith(uiAssetsFull + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                return true;
            }

            if (string.Equals(full, outerCatFull, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(full, innerCatFull, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(full, guideFull, StringComparison.OrdinalIgnoreCase))
            {
                return !allowCatalogAndUiAssetsRead;
            }
            return false;
        }

        /// <summary>
        /// 核心系统文件自保锁：同时校验逻辑路径与内核解析后的真实物理路径，防止软链接/联接点借壳读写程序自身、账本、UI源码及目录文件。
        /// </summary>
        private static bool IsProtectedSystemPath(string targetPath, bool allowCatalogAndUiAssetsRead)
        {
            if (string.IsNullOrEmpty(targetPath)) return true;
            try
            {
                string full = Path.GetFullPath(targetPath).TrimEnd('\\', '/');
                if (IsProtectedNormalizedPath(full, allowCatalogAndUiAssetsRead)) return true;
                string real = ResolveRealPath(full);
                if (!string.IsNullOrEmpty(real) && !string.Equals(real, full, StringComparison.OrdinalIgnoreCase))
                {
                    if (IsProtectedNormalizedPath(real, allowCatalogAndUiAssetsRead)) return true;
                }
            }
            catch
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// 扫描外层表目录，将所有以 _ 开头的新文件或子文件夹自动收容进 .vault 对应分类下（带防覆盖保护）
        /// </summary>
        private static void SweepUnderscoreItems()
        {
            try
            {
                // 1. 扫描根目录下的松散 _ 文件或 _ 分类文件夹（严格跳过任何软链接/联接点）
                string[] rootEntries = Directory.GetFileSystemEntries(RootDir);
                foreach (string entry in rootEntries)
                {
                    if (IsReparsePointPath(entry)) continue;
                    string name = Path.GetFileName(entry);
                    if (string.IsNullOrEmpty(name) || name.StartsWith(".")) continue;

                    if (name.StartsWith("_"))
                    {
                        bool isEntryDir = Directory.Exists(entry);
                        string cleanName = SanitizeSingleSegmentName(StripLeadingUnderscores(name, isEntryDir));
                        if (string.IsNullOrEmpty(cleanName) || IsReservedRootEntry(cleanName)) continue;
                        if (isEntryDir)
                        {
                            // 如果根目录下建了 _分类名，将该文件夹下的所有内容收容进 .vault\<cleanName>
                            string targetVaultCat = Path.Combine(VaultDir, cleanName);
                            Directory.CreateDirectory(targetVaultCat);
                            string[] children = Directory.GetFileSystemEntries(entry);
                            foreach (string child in children)
                            {
                                if (IsReparsePointPath(child)) continue;
                                string rawChildName = Path.GetFileName(child);
                                string childName = StripLeadingUnderscores(rawChildName, Directory.Exists(child));
                                if (string.IsNullOrEmpty(childName)) childName = "未命名藏品";
                                bool renamed;
                                string movedPath = MovePhysicalEntrySafe(child, targetVaultCat, childName, out renamed);
                                string finalChildName = Path.GetFileName(movedPath);
                                MigrateLedgerKeyTree(GetInternalKey("outer", name, rawChildName), GetInternalKey("inner", cleanName, finalChildName), false);
                                LogEvent("自动收容里区项: " + name + "/" + rawChildName + " -> .vault/" + cleanName + "/" + finalChildName);
                            }
                            if (Directory.GetFileSystemEntries(entry).Length == 0)
                            {
                                Directory.Delete(entry, false);
                            }
                        }
                        else if (File.Exists(entry))
                        {
                            // 根目录下的 _文件.md -> 归入 .vault\首个分类\
                            string defaultCat = Ledger.categoryOrder.Count > 0 ? Ledger.categoryOrder[0] : "未分类";
                            string targetVaultCat = Path.Combine(VaultDir, defaultCat);
                            bool renamed;
                            string movedPath = MovePhysicalEntrySafe(entry, targetVaultCat, cleanName, out renamed);
                            LogEvent("自动收容根目录里区文件: " + name + " -> .vault/" + defaultCat + "/" + Path.GetFileName(movedPath));
                        }
                    }
                }

                // 2. 扫描每个外层分类目录内部的 _ 开头文件或子文件夹（含一级子目录内部，严格跳过软链接/联接点）
                string[] outerDirs = Directory.GetDirectories(RootDir);
                foreach (string catDir in outerDirs)
                {
                    if (IsReparsePointPath(catDir)) continue;
                    string catName = Path.GetFileName(catDir);
                    if (IsReservedRootEntry(catName)) continue;

                    string[] items = Directory.GetFileSystemEntries(catDir);
                    foreach (string itemPath in items)
                    {
                        if (IsReparsePointPath(itemPath)) continue;
                        string itemName = Path.GetFileName(itemPath);
                        if (string.IsNullOrEmpty(itemName) || itemName.StartsWith(".")) continue;

                        if (itemName.StartsWith("_"))
                        {
                            string cleanItemName = StripLeadingUnderscores(itemName, Directory.Exists(itemPath));
                            if (string.IsNullOrEmpty(cleanItemName)) cleanItemName = "未命名藏品";
                            string targetVaultCat = Path.Combine(VaultDir, catName);
                            bool renamed;
                            string oldKey = GetInternalKey("outer", catName, itemName);
                            string movedPath = MovePhysicalEntrySafe(itemPath, targetVaultCat, cleanItemName, out renamed);
                            string finalName = Path.GetFileName(movedPath);
                            string newKey = GetInternalKey("inner", catName, finalName);

                            MigrateLedgerKeyTree(oldKey, newKey, false);
                            LogEvent(string.Format("自动收容 [_前缀] 入里书架: {0}\\{1} -> .vault\\{0}\\{2}{3}",
                                catName, itemName, finalName, renamed ? " (同名防覆盖已自动改名)" : ""));
                        }
                        else if (Directory.Exists(itemPath))
                        {
                            // 扫描子目录内部的 _ 前缀文件
                            string[] subEntries = Directory.GetFileSystemEntries(itemPath);
                            foreach (string subEntry in subEntries)
                            {
                                if (IsReparsePointPath(subEntry)) continue;
                                string subEntryName = Path.GetFileName(subEntry);
                                if (string.IsNullOrEmpty(subEntryName) || subEntryName.StartsWith(".")) continue;
                                if (subEntryName.StartsWith("_"))
                                {
                                    string cleanSubName = StripLeadingUnderscores(subEntryName, Directory.Exists(subEntry));
                                    if (string.IsNullOrEmpty(cleanSubName)) cleanSubName = "未命名藏品";
                                    string targetVaultSub = Path.Combine(Path.Combine(VaultDir, catName), itemName);
                                    bool renamed;
                                    string oldKey = GetInternalKey("outer", catName, itemName + "\\" + subEntryName);
                                    string movedPath = MovePhysicalEntrySafe(subEntry, targetVaultSub, cleanSubName, out renamed);
                                    string finalName = Path.GetFileName(movedPath);
                                    string newKey = GetInternalKey("inner", catName, itemName + "\\" + finalName);

                                    MigrateLedgerKeyTree(oldKey, newKey, false);
                                    string dstSubKey = GetInternalKey("inner", catName, itemName);
                                    ItemMeta sm;
                                    if (!Ledger.metaByKey.TryGetValue(dstSubKey, out sm) || sm == null) sm = new ItemMeta();
                                    if (string.IsNullOrEmpty(sm.folderMode)) sm.folderMode = "subdir";
                                    Ledger.metaByKey[dstSubKey] = sm;
                                    LogEvent(string.Format("自动收容子目录 [_前缀] 入里书架: {0}\\{1}\\{2} -> .vault\\{0}\\{1}\\{3}",
                                        catName, itemName, subEntryName, finalName));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogEvent("自动清扫 _ 文件异常: " + ex.Message);
            }
        }

        private static string GetInternalKey(string zone, string category, string name)
        {
            return zone + "::" + category + "::" + name;
        }

        private static string SanitizeRelativeSubPath(string relPath)
        {
            if (string.IsNullOrEmpty(relPath)) return "";
            string[] rawParts = relPath.Split(new char[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> cleanParts = new List<string>();
            foreach (string p in rawParts)
            {
                string seg = SanitizeSingleSegmentName(p);
                if (string.IsNullOrEmpty(seg) || seg.StartsWith(".")) return "";
                cleanParts.Add(seg);
            }
            if (cleanParts.Count == 0 || cleanParts.Count > 3) return "";
            return string.Join("\\", cleanParts.ToArray());
        }

        private static bool ShouldExpandAsSubfolder(string dirPath, ItemMeta meta)
        {
            if (meta != null)
            {
                if (string.Equals(meta.folderMode, "bundle", StringComparison.OrdinalIgnoreCase)) return false;
                if (string.Equals(meta.folderMode, "subdir", StringComparison.OrdinalIgnoreCase)) return true;
                if (!string.IsNullOrEmpty(meta.entry)) return false;
            }

            try
            {
                string[] entries = Directory.GetFileSystemEntries(dirPath);
                if (entries.Length == 0) return true;
                if (entries.Length > 24) return false;

                HashSet<string> bundleFolderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "x86", "x64", "bin", "lib", "libs", "node_modules", ".git", "psd", "assets", "resources",
                    "ja-JP", "zh-Hans", "zh-CN", "en-US", "ko-KR", "ru-RU", "GameData",
                    "00_档案与头像", "01_可读对话归档_Markdown", "02_原始无损数据_Raw"
                };

                foreach (string e in entries)
                {
                    string fn = Path.GetFileName(e);
                    if (string.IsNullOrEmpty(fn) || fn.StartsWith(".")) continue;
                    if (Directory.Exists(e))
                    {
                        if (bundleFolderNames.Contains(fn)) return false;
                    }
                    else
                    {
                        string ext = Path.GetExtension(e).ToLowerInvariant();
                        if (ext == ".exe" || ext == ".dll" || ext == ".bat" || ext == ".cmd" ||
                            ext == ".ps1" || ext == ".py" || ext == ".moc3" || ext == ".pck" || ext == ".pak" ||
                            fn.EndsWith(".model3.json", StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPureDocumentLeafFolder(string dirPath)
        {
            try
            {
                string[] subDirs = Directory.GetDirectories(dirPath);
                if (subDirs.Length > 0) return false;
                string[] files = Directory.GetFiles(dirPath);
                if (files.Length == 0 || files.Length > 16) return false;
                foreach (string f in files)
                {
                    string fn = Path.GetFileName(f);
                    if (fn.StartsWith(".")) continue;
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".md" && ext != ".txt" && ext != ".json" && ext != ".yaml" && ext != ".yml" &&
                        ext != ".html" && ext != ".htm" && ext != ".csv" && ext != ".ini" && ext != ".list")
                    {
                        return false;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 检查从允许根目录到目标路径的每一级是否存在软链接/联接点 (ReparsePoint / Junction)，防止借壳穿墙
        /// </summary>
        private static bool HasReparsePointBelowBase(string fullTarget, string fullBase)
        {
            try
            {
                string cur = fullTarget.TrimEnd('\\', '/');
                string stop = fullBase.TrimEnd('\\', '/');
                while (!string.IsNullOrEmpty(cur) && cur.Length >= stop.Length)
                {
                    if (File.Exists(cur) || Directory.Exists(cur))
                    {
                        FileAttributes attr = File.GetAttributes(cur);
                        if ((attr & FileAttributes.ReparsePoint) != 0)
                        {
                            return true;
                        }
                    }
                    if (string.Equals(cur, stop, StringComparison.OrdinalIgnoreCase)) break;
                    string parent = Path.GetDirectoryName(cur);
                    if (string.IsNullOrEmpty(parent) || string.Equals(parent, cur, StringComparison.OrdinalIgnoreCase)) break;
                    cur = parent;
                }
            }
            catch
            {
                return true;
            }
            return false;
        }

        private static bool IsPathInsideBase(string path, string baseDir)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(baseDir)) return false;
            return string.Equals(path, baseDir, StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith(baseDir + "\\", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 院墙安全检查：仅允许访问曜蚀阁内部目录 (RootDir / VaultDir) 或已在账本挂牌登记的外部路径，
        /// 同时校验逻辑路径、ReparsePoint 与内核解析的真实物理路径边界，防止父级联接点或软链接穿墙。
        /// </summary>
        private static bool IsAllowedVaultOrExternalPath(string targetPath)
        {
            if (string.IsNullOrEmpty(targetPath)) return false;
            try
            {
                string full = Path.GetFullPath(targetPath).TrimEnd('\\', '/');
                string realFull = ResolveRealPath(full);
                string rootFull = Path.GetFullPath(RootDir).TrimEnd('\\', '/');
                string realRoot = ResolveRealPath(rootFull);
                string vaultFull = Path.GetFullPath(VaultDir).TrimEnd('\\', '/');
                string realVault = ResolveRealPath(vaultFull);

                if (IsPathInsideBase(full, rootFull) || IsPathInsideBase(full, vaultFull))
                {
                    if (HasReparsePointBelowBase(full, rootFull)) return false;
                    if (!string.IsNullOrEmpty(realFull) && !string.IsNullOrEmpty(realRoot))
                    {
                        if (!IsPathInsideBase(realFull, realRoot) && !IsPathInsideBase(realFull, realVault))
                            return false;
                    }
                    return true;
                }

                lock (SyncLock)
                {
                    foreach (ExternalItem ext in Ledger.externalItems)
                    {
                        if (string.IsNullOrEmpty(ext.externalPath)) continue;
                        string extFull = Path.GetFullPath(ext.externalPath).TrimEnd('\\', '/');
                        if (IsPathInsideBase(full, extFull))
                        {
                            if (HasReparsePointBelowBase(full, extFull)) return false;
                            string realExt = ResolveRealPath(extFull);
                            if (!string.IsNullOrEmpty(realFull) && !string.IsNullOrEmpty(realExt))
                            {
                                if (!IsPathInsideBase(realFull, realExt)) return false;
                            }
                            return true;
                        }
                    }
                }
            }
            catch
            {
                return false;
            }
            return false;
        }

        public static string ClassifyItemType(string path, bool isDir, out List<string> detectedEntries, out int childCount)
        {
            detectedEntries = new List<string>();
            childCount = 0;

            if (isDir)
            {
                if (Directory.Exists(path))
                {
                    try
                    {
                        string[] entries = Directory.GetFileSystemEntries(path);
                        childCount = entries.Length;
                        // 查找顶层或一级/二级子目录内的典型入口文件 (.exe, .bat, .cmd, .ps1, .py, .html)
                        string[] topFiles = Directory.GetFiles(path);
                        foreach (string f in topFiles)
                        {
                            string ext = Path.GetExtension(f).ToLowerInvariant();
                            string fn = Path.GetFileName(f);
                            if (ext == ".exe" || ext == ".bat" || ext == ".cmd" || ext == ".ps1" || ext == ".py" || ext == ".html" || ext == ".htm")
                            {
                                detectedEntries.Add(fn);
                            }
                        }
                        if (detectedEntries.Count == 0)
                        {
                            string[] sub1 = Directory.GetDirectories(path);
                            for (int i = 0; i < sub1.Length && i < 12; i++)
                            {
                                string s1Name = Path.GetFileName(sub1[i]);
                                if (s1Name.StartsWith(".")) continue;
                                foreach (string f in Directory.GetFiles(sub1[i]))
                                {
                                    string ext = Path.GetExtension(f).ToLowerInvariant();
                                    if (ext == ".exe" || ext == ".bat" || ext == ".cmd" || ext == ".html")
                                    {
                                        detectedEntries.Add(s1Name + "\\" + Path.GetFileName(f));
                                    }
                                }
                                foreach (string sub2 in Directory.GetDirectories(sub1[i]))
                                {
                                    string s2Name = Path.GetFileName(sub2);
                                    if (s2Name.StartsWith(".")) continue;
                                    foreach (string f in Directory.GetFiles(sub2))
                                    {
                                        string ext = Path.GetExtension(f).ToLowerInvariant();
                                        if (ext == ".exe" || ext == ".bat" || ext == ".cmd")
                                        {
                                            detectedEntries.Add(s1Name + "\\" + s2Name + "\\" + Path.GetFileName(f));
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
                return "bundle";
            }

            string e = Path.GetExtension(path).ToLowerInvariant();
            if (e == ".md" || e == ".txt" || e == ".json" || e == ".yaml" || e == ".yml" || e == ".ini" || e == ".cfg" || e == ".csv" || e == ".log" || e == ".list" || e == ".xml")
                return "text";
            if (e == ".html" || e == ".htm")
                return "html";
            if (e == ".mp4" || e == ".webm" || e == ".mkv" || e == ".avi" || e == ".mov" ||
                e == ".mp3" || e == ".wav" || e == ".ogg" || e == ".flac" || e == ".m4a" ||
                e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".gif" || e == ".webp" || e == ".bmp" || e == ".svg")
                return "media";
            if (e == ".exe" || e == ".bat" || e == ".cmd" || e == ".ps1" || e == ".py" || e == ".js" ||
                e == ".zip" || e == ".7z" || e == ".rar" || e == ".lpk" || e == ".moc3")
                return "tool";

            return "other";
        }

        public static string GetReadableTypeBadge(string itemType, bool isExternal, bool isDir)
        {
            if (isExternal)
            {
                return isDir ? "🔗 外部挂牌目录" : "🔗 外部挂牌文件";
            }
            switch (itemType)
            {
                case "text": return "📝 文本与设定";
                case "html": return "🌐 网页阅览器";
                case "media": return "🎬 媒体资产";
                case "tool": return "⚙️ 工具/程序包";
                case "bundle": return "🧰 工具箱/套装";
                default: return "📦 藏品文件";
            }
        }

        public static void SyncAndGenerateCatalogs()
        {
            lock (SyncLock)
            {
                if (IsSyncing) return;
                IsSyncing = true;
                try
                {
                    EnsureIgnoreFile();
                    SweepUnderscoreItems();

                    List<CategoryDto> categories;
                    List<SubDirDto> subDirs;
                    List<BookItemDto> allItems;
                    CollectAllState(out categories, out subDirs, out allItems);

                    GenerateMarkdownCatalogs(categories, allItems);
                    SaveLedger();
                }
                catch (Exception ex)
                {
                    LogEvent("同步目录异常: " + ex.Message);
                }
                finally
                {
                    IsSyncing = false;
                }
            }
        }

        private static void CollectAllState(out List<CategoryDto> categories, out List<SubDirDto> subDirs, out List<BookItemDto> allItems)
        {
            HashSet<string> catSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> orderedCats = new List<string>();

            foreach (string c in Ledger.categoryOrder)
            {
                if (!string.IsNullOrEmpty(c) && !IsReservedRootEntry(c) && !catSet.Contains(c))
                {
                    catSet.Add(c);
                    orderedCats.Add(c);
                }
            }

            if (Directory.Exists(RootDir))
            {
                foreach (string d in Directory.GetDirectories(RootDir))
                {
                    if (IsReparsePointPath(d)) continue;
                    string cn = Path.GetFileName(d);
                    if (!IsReservedRootEntry(cn) && !catSet.Contains(cn))
                    {
                        catSet.Add(cn);
                        orderedCats.Add(cn);
                    }
                }
            }

            if (Directory.Exists(VaultDir))
            {
                foreach (string d in Directory.GetDirectories(VaultDir))
                {
                    if (IsReparsePointPath(d)) continue;
                    string cn = Path.GetFileName(d);
                    if (cn.Equals("ui", StringComparison.OrdinalIgnoreCase) || cn.Equals("src", StringComparison.OrdinalIgnoreCase) || cn.StartsWith("."))
                        continue;
                    if (!catSet.Contains(cn))
                    {
                        catSet.Add(cn);
                        orderedCats.Add(cn);
                    }
                }
            }

            foreach (ExternalItem ext in Ledger.externalItems)
            {
                if (!string.IsNullOrEmpty(ext.category) && !catSet.Contains(ext.category))
                {
                    catSet.Add(ext.category);
                    orderedCats.Add(ext.category);
                }
            }

            Ledger.categoryOrder = orderedCats;
            subDirs = new List<SubDirDto>();
            allItems = new List<BookItemDto>();

            // 1. 收集表书架实体项 (E:\Gold Mine\<cat>\)
            foreach (string cat in orderedCats)
            {
                string outerCatDir = Path.Combine(RootDir, cat);
                if (Directory.Exists(outerCatDir))
                {
                    ScanPhysicalCategory(outerCatDir, cat, "outer", subDirs, allItems);
                }

                // 2. 收集里书架实体项 (E:\Gold Mine\.vault\<cat>\)
                string innerCatDir = Path.Combine(VaultDir, cat);
                if (Directory.Exists(innerCatDir))
                {
                    ScanPhysicalCategory(innerCatDir, cat, "inner", subDirs, allItems);
                }
            }

            // 3. 收集外部原位挂牌项
            foreach (ExternalItem ext in Ledger.externalItems)
            {
                bool isDir = Directory.Exists(ext.externalPath);
                bool isFile = File.Exists(ext.externalPath);
                bool exists = isDir || isFile;

                List<string> detected;
                int childCount;
                string itemType = ClassifyItemType(ext.externalPath, isDir, out detected, out childCount);

                long size = 0;
                string updated = ext.addedAt;
                try
                {
                    if (isFile)
                    {
                        FileInfo fi = new FileInfo(ext.externalPath);
                        size = fi.Length;
                        updated = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                    }
                    else if (isDir)
                    {
                        DirectoryInfo di = new DirectoryInfo(ext.externalPath);
                        updated = di.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                    }
                }
                catch { }

                string entryVal = ext.entry;
                if (string.IsNullOrEmpty(entryVal) && detected.Count > 0)
                {
                    entryVal = detected[0];
                }

                BookItemDto dto = new BookItemDto();
                dto.id = "ext_" + ext.id;
                dto.key = "ext::" + ext.id;
                dto.name = string.IsNullOrEmpty(ext.name) ? Path.GetFileName(ext.externalPath.TrimEnd('\\', '/')) : ext.name;
                dto.category = string.IsNullOrEmpty(ext.category) ? "未分类" : ext.category;
                dto.subDir = "";
                dto.zone = (ext.zone == "inner") ? "inner" : "outer";
                dto.isExternal = true;
                dto.isDirectory = isDir;
                dto.existsOnDisk = exists;
                dto.fullPath = ext.externalPath;
                dto.relPath = ext.externalPath;
                dto.itemType = itemType;
                dto.ext = isDir ? "DIR" : Path.GetExtension(ext.externalPath).ToLowerInvariant();
                dto.size = size;
                dto.updatedAt = updated;
                dto.note = ext.note ?? "";
                dto.tags = ext.tags ?? new List<string>();
                dto.entry = entryVal ?? "";
                dto.detectedEntries = detected;
                dto.childCount = childCount;

                allItems.Add(dto);
            }

            categories = new List<CategoryDto>();
            foreach (string cat in orderedCats)
            {
                int oCount = 0;
                int iCount = 0;
                foreach (BookItemDto item in allItems)
                {
                    if (string.Equals(item.category, cat, StringComparison.OrdinalIgnoreCase))
                    {
                        if (item.zone == "inner") iCount++;
                        else oCount++;
                    }
                }
                CategoryDto cDto = new CategoryDto();
                cDto.name = cat;
                cDto.outerCount = oCount;
                cDto.innerCount = iCount;
                cDto.totalCount = oCount + iCount;
                categories.Add(cDto);
            }
        }

        private static BookItemDto BuildPhysicalItemDto(string path, string catName, string subDirName, string displayPrefix, string zone)
        {
            string fileName = Path.GetFileName(path);
            bool isDir = Directory.Exists(path);
            List<string> detected;
            int childCount;
            string itemType = ClassifyItemType(path, isDir, out detected, out childCount);

            long size = 0;
            string updated = "";
            try
            {
                if (isDir)
                {
                    DirectoryInfo di = new DirectoryInfo(path);
                    updated = di.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                }
                else
                {
                    FileInfo fi = new FileInfo(path);
                    size = fi.Length;
                    updated = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                }
            }
            catch { }

            string relUnderCat = string.IsNullOrEmpty(subDirName)
                ? fileName
                : (string.IsNullOrEmpty(displayPrefix)
                    ? (subDirName + "\\" + fileName)
                    : (subDirName + "\\" + displayPrefix + "\\" + fileName));

            string key = GetInternalKey(zone, catName, relUnderCat);
            ItemMeta meta;
            if (!Ledger.metaByKey.TryGetValue(key, out meta) || meta == null)
            {
                meta = new ItemMeta();
            }

            string entryVal = meta.entry;
            if (string.IsNullOrEmpty(entryVal) && detected.Count > 0)
            {
                entryVal = detected[0];
            }

            string relPath = (zone == "inner")
                ? Path.Combine(".vault", Path.Combine(catName, relUnderCat))
                : Path.Combine(catName, relUnderCat);

            BookItemDto dto = new BookItemDto();
            dto.id = Convert.ToBase64String(Encoding.UTF8.GetBytes(key));
            dto.key = key;
            dto.name = string.IsNullOrEmpty(displayPrefix) ? fileName : (displayPrefix + " / " + fileName);
            dto.category = catName;
            dto.subDir = subDirName ?? "";
            dto.zone = zone;
            dto.isExternal = false;
            dto.isDirectory = isDir;
            dto.existsOnDisk = true;
            dto.fullPath = path;
            dto.relPath = relPath;
            dto.itemType = itemType;
            dto.ext = isDir ? "DIR" : Path.GetExtension(path).ToLowerInvariant();
            dto.size = size;
            dto.updatedAt = updated;
            dto.note = meta.note ?? "";
            dto.tags = meta.tags ?? new List<string>();
            dto.entry = entryVal ?? "";
            dto.detectedEntries = detected;
            dto.childCount = childCount;
            return dto;
        }

        private static void ScanPhysicalCategory(string catDir, string catName, string zone, List<SubDirDto> subDirs, List<BookItemDto> allItems)
        {
            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(catDir);
            }
            catch
            {
                return;
            }

            foreach (string path in entries)
            {
                if (IsReparsePointPath(path)) continue;
                string name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(name) || name.StartsWith(".")) continue;

                bool isDir = Directory.Exists(path);
                string key = GetInternalKey(zone, catName, name);
                ItemMeta meta;
                if (!Ledger.metaByKey.TryGetValue(key, out meta) || meta == null)
                {
                    meta = new ItemMeta();
                }

                if (isDir && ShouldExpandAsSubfolder(path, meta))
                {
                    int subCount = 0;
                    try
                    {
                        string[] subEntries = Directory.GetFileSystemEntries(path);
                        foreach (string subPath in subEntries)
                        {
                            if (IsReparsePointPath(subPath)) continue;
                            string subName = Path.GetFileName(subPath);
                            if (string.IsNullOrEmpty(subName) || subName.StartsWith(".")) continue;

                            if (Directory.Exists(subPath) && IsPureDocumentLeafFolder(subPath))
                            {
                                foreach (string leafFile in Directory.GetFiles(subPath))
                                {
                                    if (IsReparsePointPath(leafFile)) continue;
                                    string leafName = Path.GetFileName(leafFile);
                                    if (string.IsNullOrEmpty(leafName) || leafName.StartsWith(".")) continue;
                                    allItems.Add(BuildPhysicalItemDto(leafFile, catName, name, subName, zone));
                                    subCount++;
                                }
                            }
                            else
                            {
                                allItems.Add(BuildPhysicalItemDto(subPath, catName, name, "", zone));
                                subCount++;
                            }
                        }
                    }
                    catch { }

                    SubDirDto sd = new SubDirDto();
                    sd.key = key;
                    sd.name = name;
                    sd.category = catName;
                    sd.zone = zone;
                    sd.fullPath = path;
                    sd.note = meta.note ?? "";
                    sd.itemCount = subCount;
                    subDirs.Add(sd);
                    continue;
                }

                allItems.Add(BuildPhysicalItemDto(path, catName, "", "", zone));
            }
        }

        private static string FormatCatalogItemTitle(BookItemDto it)
        {
            if (string.IsNullOrEmpty(it.subDir)) return it.name;
            return it.subDir + " / " + it.name;
        }

        private static void GenerateMarkdownCatalogs(List<CategoryDto> categories, List<BookItemDto> allItems)
        {
            // 1. 生成外层《目录.md》（仅包含 zone == "outer"，净网级防爆）
            StringBuilder sbOuter = new StringBuilder();
            sbOuter.AppendLine("# 🏛️ Gold Mine 曜蚀阁 · 总目录 (表书架)");
            sbOuter.AppendLine();
            sbOuter.AppendLine("> **🤖 致 AI 助手的读取与归档指南（请先读此段）**");
            sbOuter.AppendLine("> 1. **按需精准读取**：本目录为 `" + RootDir + "` 曜蚀阁的净网索引单。请像人类查阅图书目录一样，先根据下方的【分类】、【名称】与【说明】定位所需项目，**仅读取任务明确需要的具体文件路径**，切勿盲目遍历或全量扫描无关目录。");
            sbOuter.AppendLine("> 2. **工具箱调用**：标记为 `🧰 工具箱/套装` 或 `🔗 外部挂牌` 的项目对应一个完整目录，若标注了 `主程序/入口`，可直接结合该路径执行或分析。");
            sbOuter.AppendLine("> 3. **新产出归档规矩**：当你生成了新的角色卡、文本或工具需要入库时，请直接保存在对应分类目录 `" + RootDir + "\\<分类名>\\<文件名>.md`（或对应子目录）下，曜蚀阁后台会自动将其编入本目录。");
            sbOuter.AppendLine();
            sbOuter.AppendLine("- **曜蚀阁根目录**：`" + RootDir + "`");
            sbOuter.AppendLine("- **最后同步时间**：`" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "`");
            sbOuter.AppendLine();

            int totalOuter = 0;
            foreach (CategoryDto cat in categories)
            {
                List<BookItemDto> catOuterItems = new List<BookItemDto>();
                foreach (BookItemDto it in allItems)
                {
                    if (string.Equals(it.category, cat.name, StringComparison.OrdinalIgnoreCase) && it.zone == "outer")
                    {
                        catOuterItems.Add(it);
                    }
                }
                totalOuter += catOuterItems.Count;

                sbOuter.AppendLine("## 📂 " + cat.name + " (" + catOuterItems.Count + ")");
                sbOuter.AppendLine();
                if (catOuterItems.Count == 0)
                {
                    sbOuter.AppendLine("*(当前分类暂无表书架藏品 — 可直接将文件放入 `" + Path.Combine(RootDir, cat.name) + "`)*");
                    sbOuter.AppendLine();
                    continue;
                }

                sbOuter.AppendLine("| 藏品名称 | 类型 | 路径 | 入口/启动项 | 标签与备注说明 |");
                sbOuter.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
                foreach (BookItemDto it in catOuterItems)
                {
                    string badge = GetReadableTypeBadge(it.itemType, it.isExternal, it.isDirectory);
                    string entryCol = string.IsNullOrEmpty(it.entry) ? "-" : "`" + EscapeMdTable(it.entry) + "`";
                    string tagStr = (it.tags != null && it.tags.Count > 0) ? ("[" + string.Join(", ", it.tags.ToArray()) + "] ") : "";
                    string noteStr = string.IsNullOrEmpty(it.note) ? "-" : EscapeMdTable(it.note);
                    string combinedNote = string.IsNullOrEmpty(tagStr) ? noteStr : (EscapeMdTable(tagStr) + (noteStr == "-" ? "" : noteStr));
                    sbOuter.AppendLine(string.Format("| **{0}** | {1} | `{2}` | {3} | {4} |",
                        EscapeMdTable(FormatCatalogItemTitle(it)), badge, EscapeMdTable(it.fullPath), entryCol, combinedNote));
                }
                sbOuter.AppendLine();
            }

            WriteTextAtomic(OuterCatalogPath, sbOuter.ToString(), false);

            // 2. 生成内层《.vault\目录_里.md》（包含里书架全部藏品 + 表书架参考）
            StringBuilder sbInner = new StringBuilder();
            sbInner.AppendLine("# 🌙 Gold Mine 曜蚀阁 · 里目录 (含高能私密区 & 外部挂牌)");
            sbInner.AppendLine();
            sbInner.AppendLine("> **🤖 致 AI 助手的读取与归档指南**");
            sbInner.AppendLine("> 1. 你已获准访问 `" + VaultDir + "` 里书架索引。请根据下方目录精准读取任务指定的文件或工具箱路径。");
            sbInner.AppendLine("> 2. 若需将新的高能产出归入里书架，可直接保存至 `" + VaultDir + "\\<分类名>\\<文件名>.md`（或在外层分类下以 `_` 前缀命名文件，系统会自动将其收容至 `.vault`）。");
            sbInner.AppendLine();
            sbInner.AppendLine("- **里书架实体仓路径**：`" + VaultDir + "`");
            sbInner.AppendLine("- **最后同步时间**：`" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "`");
            sbInner.AppendLine();

            sbInner.AppendLine("## 🌙 第一部分：里书架专享藏品 (.vault & 里区挂牌)");
            sbInner.AppendLine();

            foreach (CategoryDto cat in categories)
            {
                List<BookItemDto> catInnerItems = new List<BookItemDto>();
                foreach (BookItemDto it in allItems)
                {
                    if (string.Equals(it.category, cat.name, StringComparison.OrdinalIgnoreCase) && it.zone == "inner")
                    {
                        catInnerItems.Add(it);
                    }
                }

                sbInner.AppendLine("### 📂 " + cat.name + " · 里区 (" + catInnerItems.Count + ")");
                sbInner.AppendLine();
                if (catInnerItems.Count == 0)
                {
                    sbInner.AppendLine("*(当前分类暂无里书架藏品 — 实体路径 `" + Path.Combine(VaultDir, cat.name) + "`)*");
                    sbInner.AppendLine();
                    continue;
                }

                sbInner.AppendLine("| 藏品名称 | 类型 | 完整路径 | 入口/启动项 | 标签与备注说明 |");
                sbInner.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
                foreach (BookItemDto it in catInnerItems)
                {
                    string badge = GetReadableTypeBadge(it.itemType, it.isExternal, it.isDirectory);
                    string entryCol = string.IsNullOrEmpty(it.entry) ? "-" : "`" + EscapeMdTable(it.entry) + "`";
                    string tagStr = (it.tags != null && it.tags.Count > 0) ? ("[" + string.Join(", ", it.tags.ToArray()) + "] ") : "";
                    string noteStr = string.IsNullOrEmpty(it.note) ? "-" : EscapeMdTable(it.note);
                    string combinedNote = string.IsNullOrEmpty(tagStr) ? noteStr : (EscapeMdTable(tagStr) + (noteStr == "-" ? "" : noteStr));
                    sbInner.AppendLine(string.Format("| **{0}** | {1} | `{2}` | {3} | {4} |",
                        EscapeMdTable(FormatCatalogItemTitle(it)), badge, EscapeMdTable(it.fullPath), entryCol, combinedNote));
                }
                sbInner.AppendLine();
            }

            sbInner.AppendLine("---");
            sbInner.AppendLine();
            sbInner.AppendLine("## ☀️ 第二部分：表书架通用藏品同步索引");
            sbInner.AppendLine();
            foreach (CategoryDto cat in categories)
            {
                List<BookItemDto> catOuterItems = new List<BookItemDto>();
                foreach (BookItemDto it in allItems)
                {
                    if (string.Equals(it.category, cat.name, StringComparison.OrdinalIgnoreCase) && it.zone == "outer")
                    {
                        catOuterItems.Add(it);
                    }
                }
                if (catOuterItems.Count == 0) continue;

                sbInner.AppendLine("### 📂 " + cat.name + " · 表区 (" + catOuterItems.Count + ")");
                sbInner.AppendLine();
                sbInner.AppendLine("| 藏品名称 | 类型 | 完整路径 | 入口/启动项 | 标签与备注说明 |");
                sbInner.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
                foreach (BookItemDto it in catOuterItems)
                {
                    string badge = GetReadableTypeBadge(it.itemType, it.isExternal, it.isDirectory);
                    string entryCol = string.IsNullOrEmpty(it.entry) ? "-" : "`" + EscapeMdTable(it.entry) + "`";
                    string tagStr = (it.tags != null && it.tags.Count > 0) ? ("[" + string.Join(", ", it.tags.ToArray()) + "] ") : "";
                    string noteStr = string.IsNullOrEmpty(it.note) ? "-" : EscapeMdTable(it.note);
                    string combinedNote = string.IsNullOrEmpty(tagStr) ? noteStr : (EscapeMdTable(tagStr) + (noteStr == "-" ? "" : noteStr));
                    sbInner.AppendLine(string.Format("| **{0}** | {1} | `{2}` | {3} | {4} |",
                        EscapeMdTable(FormatCatalogItemTitle(it)), badge, EscapeMdTable(it.fullPath), entryCol, combinedNote));
                }
                sbInner.AppendLine();
            }

            WriteTextAtomic(InnerCatalogPath, sbInner.ToString(), false);
        }

        private static string EscapeMdTable(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return input.Replace("\r", " ").Replace("\n", " ").Replace("|", "\\|").Trim();
        }

        private static void StartWatcher()
        {
            try
            {
                DebounceTimer = new System.Threading.Timer(delegate(object state)
                {
                    SyncAndGenerateCatalogs();
                }, null, Timeout.Infinite, Timeout.Infinite);

                OuterWatcher = new FileSystemWatcher(RootDir);
                OuterWatcher.IncludeSubdirectories = true;
                OuterWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite;

                Func<string, bool> isIgnoredWatchPath = delegate(string fullP)
                {
                    if (string.IsNullOrEmpty(fullP)) return true;
                    return fullP.IndexOf("目录.md", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           fullP.IndexOf("目录_里.md", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           fullP.IndexOf(".goldmine.json", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           fullP.IndexOf(".gm_tmp_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           fullP.IndexOf(".ignore", StringComparison.OrdinalIgnoreCase) >= 0;
                };

                FileSystemEventHandler handler = delegate(object sender, FileSystemEventArgs e)
                {
                    if (isIgnoredWatchPath(e.FullPath)) return;
                    DebounceTimer.Change(600, Timeout.Infinite);
                };

                RenamedEventHandler renHandler = delegate(object sender, RenamedEventArgs e)
                {
                    if (isIgnoredWatchPath(e.FullPath) && isIgnoredWatchPath(e.OldFullPath)) return;
                    DebounceTimer.Change(600, Timeout.Infinite);
                };

                OuterWatcher.Created += handler;
                OuterWatcher.Deleted += handler;
                OuterWatcher.Changed += handler;
                OuterWatcher.Renamed += renHandler;
                OuterWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                LogEvent("启动文件监控异常: " + ex.Message);
            }
        }

        private static void SetupTrayIcon()
        {
            TrayIcon = new NotifyIcon();
            string trayTitle = "Gold Mine · 曜蚀阁 (" + RootDir + ")";
            if (trayTitle.Length > 60) trayTitle = trayTitle.Substring(0, 60);
            TrayIcon.Text = trayTitle;
            TrayIcon.Icon = CreateGoldIcon();
            TrayIcon.Visible = true;

            ContextMenu menu = new ContextMenu();
            MenuItem openUiItem = new MenuItem("🏛️ 打开曜蚀阁界面 (" + ServerUrl + ")", delegate(object s, EventArgs e)
            {
                try { Process.Start(ServerUrl); } catch { }
            });
            openUiItem.DefaultItem = true;

            MenuItem syncNowItem = new MenuItem("🔄 立即扫描并同步目录 (含收容 _ 文件)", delegate(object s, EventArgs e)
            {
                SyncAndGenerateCatalogs();
                TrayIcon.ShowBalloonTip(1500, "Gold Mine · 曜蚀阁", "已完成表/里目录同步与 _ 文件收容！", ToolTipIcon.Info);
            });

            MenuItem openFolderItem = new MenuItem("📂 打开曜蚀阁根目录 (" + RootDir + ")", delegate(object s, EventArgs e)
            {
                try { OpenExplorerAndPopToFront(RootDir, false); } catch { }
            });

            MenuItem exitItem = new MenuItem("❌ 退出曜蚀阁后台服务", delegate(object s, EventArgs e)
            {
                TrayIcon.Visible = false;
                try { if (Listener != null && Listener.IsListening) Listener.Stop(); } catch { }
                Environment.Exit(0);
            });

            menu.MenuItems.Add(openUiItem);
            menu.MenuItems.Add(syncNowItem);
            menu.MenuItems.Add(openFolderItem);
            menu.MenuItems.Add("-");
            menu.MenuItems.Add(exitItem);

            TrayIcon.ContextMenu = menu;
            TrayIcon.DoubleClick += delegate(object s, EventArgs e)
            {
                try { Process.Start(ServerUrl); } catch { }
            };
        }

        private static Icon CreateGoldIcon()
        {
            Bitmap bmp = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (LinearGradientBrush brush = new LinearGradientBrush(new Rectangle(2, 2, 28, 28), Color.FromArgb(245, 198, 92), Color.FromArgb(180, 120, 28), 45f))
                {
                    g.FillEllipse(brush, 2, 2, 28, 28);
                }
                using (Pen pen = new Pen(Color.FromArgb(255, 238, 170), 2f))
                {
                    g.DrawEllipse(pen, 3, 3, 26, 26);
                }
                using (Font f = new Font("Microsoft YaHei", 13f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Brush tb = new SolidBrush(Color.FromArgb(30, 22, 10)))
                {
                    SizeF sf = g.MeasureString("阁", f);
                    g.DrawString("阁", f, tb, (32 - sf.Width) / 2f, (32 - sf.Height) / 2f + 1f);
                }
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        private static void StartHttpServer()
        {
            Thread serverThread = new Thread(delegate()
            {
                try
                {
                    Listener = new HttpListener();
                    Listener.Prefixes.Add(ServerUrl);
                    Listener.Start();
                    LogEvent("本地服务已启动: " + ServerUrl);

                    while (Listener.IsListening)
                    {
                        try
                        {
                            HttpListenerContext ctx = Listener.GetContext();
                            ThreadPool.QueueUserWorkItem(delegate(object state)
                            {
                                HandleHttpRequest((HttpListenerContext)state);
                            }, ctx);
                        }
                        catch
                        {
                            if (!Listener.IsListening) break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogEvent("HTTP 服务启动失败: " + ex.Message);
                }
            });
            serverThread.IsBackground = true;
            serverThread.Start();
        }

        private static string GetUtf8QueryParam(HttpListenerRequest req, string key)
        {
            if (req == null || req.Url == null || string.IsNullOrEmpty(req.Url.Query)) return "";
            var nvc = HttpUtility.ParseQueryString(req.Url.Query, Encoding.UTF8);
            return nvc[key] ?? "";
        }

        private static void HandleHttpRequest(HttpListenerContext ctx)
        {
            HttpListenerRequest req = ctx.Request;
            HttpListenerResponse res = ctx.Response;
            JavaScriptSerializer ser = new JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;

            res.Headers["X-Content-Type-Options"] = "nosniff";
            res.Headers["Referrer-Policy"] = "no-referrer";

            string hostHeader = req.Headers["Host"];
            string allowedHost1 = "127.0.0.1:" + Port;
            string allowedHost2 = "localhost:" + Port;
            if (string.IsNullOrEmpty(hostHeader) ||
                (!string.Equals(hostHeader, allowedHost1, StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(hostHeader, allowedHost2, StringComparison.OrdinalIgnoreCase)))
            {
                WriteError(res, ser, "Host 校验拦截：拒绝非本机回环 Host（防 DNS 重绑定）", 403);
                return;
            }

            string allowedOrigin1 = ServerUrl.TrimEnd('/');
            string allowedOrigin2 = "http://localhost:" + Port;
            string origin = req.Headers["Origin"];
            if (!string.IsNullOrEmpty(origin))
            {
                if (string.Equals(origin, allowedOrigin1, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(origin, allowedOrigin2, StringComparison.OrdinalIgnoreCase))
                {
                    res.Headers.Add("Access-Control-Allow-Origin", origin);
                    res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                    res.Headers.Add("Access-Control-Allow-Headers", "Content-Type, X-GoldMine-Token");
                }
                else
                {
                    WriteError(res, ser, "跨域安全拦截：拒绝外部网页访问本地藏宝阁接口", 403);
                    return;
                }
            }

            if (req.HttpMethod == "OPTIONS")
            {
                res.StatusCode = 200;
                res.Close();
                return;
            }

            string path = req.Url.AbsolutePath;
            string secFetchSite = req.Headers["Sec-Fetch-Site"];
            if (!string.IsNullOrEmpty(secFetchSite) &&
                string.Equals(secFetchSite, "cross-site", StringComparison.OrdinalIgnoreCase))
            {
                bool isTopDocNav = req.HttpMethod == "GET" &&
                                   (path == "/" || path == "/index.html") &&
                                   string.Equals(req.Headers["Sec-Fetch-Dest"], "document", StringComparison.OrdinalIgnoreCase);
                if (!isTopDocNav)
                {
                    WriteError(res, ser, "跨站安全拦截：拒绝外部站点或隔离沙盒调用本地藏宝阁接口", 403);
                    return;
                }
            }

            try
            {
                if (path == "/" || path == "/index.html")
                {
                    if (File.Exists(UiPath))
                    {
                        res.Headers["X-Frame-Options"] = "SAMEORIGIN";
                        res.Headers["Content-Security-Policy"] = "frame-ancestors 'self'; base-uri 'self'; object-src 'none'";
                        res.Headers["Cache-Control"] = "no-store";
                        string html = File.ReadAllText(UiPath, Encoding.UTF8);
                        string tokenScript = "<script>window.__GOLDMINE_TOKEN__ = \"" + SessionToken + "\";</script></head>";
                        int headIdx = html.IndexOf("</head>", StringComparison.Ordinal);
                        if (headIdx >= 0)
                        {
                            html = html.Substring(0, headIdx) + tokenScript + html.Substring(headIdx + 7);
                        }
                        byte[] bytes = Encoding.UTF8.GetBytes(html);
                        res.ContentType = "text/html; charset=utf-8";
                        res.ContentLength64 = bytes.Length;
                        res.OutputStream.Write(bytes, 0, bytes.Length);
                        res.Close();
                        return;
                    }
                    else
                    {
                        WriteTextResponse(res, "UI 文件未找到: " + UiPath, "text/plain; charset=utf-8", 404);
                        return;
                    }
                }

                // 入口双牌皮肤静态立绘（严格白名单限定两张内置皮肤图，不接受任意路径）
                if (path == "/api/gate-asset" && req.HttpMethod == "GET")
                {
                    string assetName = GetUtf8QueryParam(req, "name");
                    if (assetName != "starlight.png" && assetName != "obsidian.png")
                    {
                        WriteError(res, ser, "仅允许加载内置门扉立绘", 403);
                        return;
                    }
                    string assetPath = Path.Combine(Path.Combine(Path.Combine(VaultDir, "ui"), "assets"), assetName);
                    if (!File.Exists(assetPath))
                    {
                        res.StatusCode = 404;
                        res.Close();
                        return;
                    }
                    ServeStaticFile(res, assetPath);
                    return;
                }

                // 单路径、60秒有效期、即看即焚的预览票兑换出口（URL 中不暴露任何主口令或磁盘路径）
                if (path == "/api/raw" && req.HttpMethod == "GET")
                {
                    string ticket = GetUtf8QueryParam(req, "t");
                    string filePath = ConsumePreviewTicket(ticket);
                    if (string.IsNullOrEmpty(filePath) || !IsAllowedVaultOrExternalPath(filePath) || IsProtectedSystemPath(filePath, true))
                    {
                        WriteError(res, ser, "预览票无效、已过期或已作废", 403);
                        return;
                    }
                    if (!File.Exists(filePath))
                    {
                        res.StatusCode = 404;
                        res.Close();
                        return;
                    }
                    res.Headers["Cache-Control"] = "no-store";
                    ServeStaticFile(res, filePath);
                    return;
                }

                // 其余所有 /api/* 接口仅接受 HTTP Header 中的 X-GoldMine-Token 主控口令（禁止 URL 参数传口令）
                if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                {
                    string headerToken = req.Headers["X-GoldMine-Token"];
                    if (!FixedTimeEquals(headerToken, SessionToken))
                    {
                        WriteError(res, ser, "鉴权拦截：缺少或无效的 X-GoldMine-Token 主控口令，拒绝访问", 403);
                        return;
                    }
                }

                if (path == "/api/state")
                {
                    SyncAndGenerateCatalogs();
                    List<CategoryDto> categories;
                    List<SubDirDto> subDirs;
                    List<BookItemDto> allItems;
                    List<string> logsCopy;
                    lock (SyncLock)
                    {
                        CollectAllState(out categories, out subDirs, out allItems);
                    }
                    lock (RecentLogs)
                    {
                        logsCopy = new List<string>(RecentLogs);
                    }
                    Dictionary<string, object> payload = new Dictionary<string, object>();
                    payload["ok"] = true;
                    payload["rootDir"] = RootDir;
                    payload["vaultDir"] = VaultDir;
                    payload["outerCatalogPath"] = OuterCatalogPath;
                    payload["innerCatalogPath"] = InnerCatalogPath;
                    payload["ignoreFilePath"] = IgnoreFilePath;
                    payload["categories"] = categories;
                    payload["subDirs"] = subDirs;
                    payload["items"] = allItems;
                    payload["logs"] = logsCopy;
                    WriteJsonResponse(res, ser.Serialize(payload));
                    return;
                }

                if (path == "/api/subdir/create" && req.HttpMethod == "POST")
                {
                    Dictionary<string, object> body = ReadJsonBody(req, ser);
                    string category = SanitizeSingleSegmentName(StripLeadingUnderscores(GetStr(body, "category"), true));
                    string zone = GetStr(body, "zone") == "inner" ? "inner" : "outer";
                    string subName = SanitizeSingleSegmentName(StripLeadingUnderscores(GetStr(body, "name"), true));
                    string note = GetStr(body, "note").Trim();

                    if (string.IsNullOrEmpty(category) || IsReservedRootEntry(category))
                    {
                        WriteError(res, ser, "所属分类无效");
                        return;
                    }
                    if (string.IsNullOrEmpty(subName) || subName.StartsWith(".") || IsReservedRootEntry(subName))
                    {
                        WriteError(res, ser, "子目录名称无效或属于保留名称");
                        return;
                    }

                    string baseRoot = (zone == "inner") ? VaultDir : RootDir;
                    string subDirFull = Path.Combine(Path.Combine(baseRoot, category), subName);
                    if (!IsAllowedVaultOrExternalPath(subDirFull) || IsProtectedSystemPath(subDirFull, false))
                    {
                        WriteError(res, ser, "越界拦截：子目录路径未通过院墙校验", 403);
                        return;
                    }

                    lock (SyncLock)
                    {
                        Directory.CreateDirectory(subDirFull);
                        string subKey = GetInternalKey(zone, category, subName);
                        ItemMeta meta;
                        if (!Ledger.metaByKey.TryGetValue(subKey, out meta) || meta == null) meta = new ItemMeta();
                        meta.folderMode = "subdir";
                        if (!string.IsNullOrEmpty(note)) meta.note = note;
                        meta.updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        Ledger.metaByKey[subKey] = meta;
                        if (!Ledger.categoryOrder.Contains(category)) Ledger.categoryOrder.Add(category);
                        SaveLedger();
                    }

                    LogEvent("新建子目录: " + category + "\\" + subName + " (" + (zone == "inner" ? "里书架" : "表书架") + ")");
                    SyncAndGenerateCatalogs();
                    WriteOk(res, ser, "已在「" + category + "」下新建子目录「" + subName + "」");
                    return;
                }

                if (path == "/api/subdir/rename" && req.HttpMethod == "POST")
                {
                    Dictionary<string, object> body = ReadJsonBody(req, ser);
                    string zone = GetStr(body, "zone") == "inner" ? "inner" : "outer";
                    string category = SanitizeSingleSegmentName(GetStr(body, "category"));
                    string oldName = SanitizeSingleSegmentName(GetStr(body, "oldName"));
                    string newName = SanitizeSingleSegmentName(StripLeadingUnderscores(GetStr(body, "newName"), true));
                    string note = body.ContainsKey("note") ? GetStr(body, "note").Trim() : null;

                    if (string.IsNullOrEmpty(category) || string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName) ||
                        IsReservedRootEntry(category) || oldName.StartsWith(".") || newName.StartsWith(".") || IsReservedRootEntry(newName))
                    {
                        WriteError(res, ser, "分类或子目录名称无效");
                        return;
                    }

                    lock (SyncLock)
                    {
                        string baseRoot = (zone == "inner") ? VaultDir : RootDir;
                        string oldDir = Path.Combine(Path.Combine(baseRoot, category), oldName);
                        string newDir = Path.Combine(Path.Combine(baseRoot, category), newName);
                        if (!IsAllowedVaultOrExternalPath(oldDir) || IsProtectedSystemPath(oldDir, false) ||
                            !IsAllowedVaultOrExternalPath(newDir) || IsProtectedSystemPath(newDir, false))
                        {
                            WriteError(res, ser, "越界拦截：子目录重命名路径未通过院墙校验", 403);
                            return;
                        }

                        if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase) && Directory.Exists(oldDir))
                        {
                            string oldSubKey = GetInternalKey(zone, category, oldName);
                            string newSubKey = GetInternalKey(zone, category, newName);
                            if (!Directory.Exists(newDir))
                            {
                                Directory.Move(oldDir, newDir);
                                MigrateLedgerKeyTree(oldSubKey, newSubKey, true);
                            }
                            else
                            {
                                MergeDirectoryAndMigrateKeys(oldDir, newDir, oldSubKey, newSubKey, false);
                                if (Ledger.metaByKey.ContainsKey(oldSubKey))
                                {
                                    if (!Ledger.metaByKey.ContainsKey(newSubKey)) Ledger.metaByKey[newSubKey] = Ledger.metaByKey[oldSubKey];
                                    Ledger.metaByKey.Remove(oldSubKey);
                                }
                            }
                        }

                        string targetSubKey = GetInternalKey(zone, category, newName);
                        ItemMeta sm;
                        if (!Ledger.metaByKey.TryGetValue(targetSubKey, out sm) || sm == null) sm = new ItemMeta();
                        sm.folderMode = "subdir";
                        if (note != null) sm.note = note;
                        Ledger.metaByKey[targetSubKey] = sm;
                        SaveLedger();
                    }

                    LogEvent("重命名/更新子目录: " + category + "\\" + oldName + " -> " + newName);
                    SyncAndGenerateCatalogs();
                    WriteOk(res, ser, "已更新子目录「" + newName + "」");
                    return;
                }

                if (path == "/api/subdir/delete" && req.HttpMethod == "POST")
                {
                    Dictionary<string, object> body = ReadJsonBody(req, ser);
                    string zone = GetStr(body, "zone") == "inner" ? "inner" : "outer";
                    string category = SanitizeSingleSegmentName(GetStr(body, "category"));
                    string name = SanitizeSingleSegmentName(GetStr(body, "name"));

                    if (string.IsNullOrEmpty(category) || string.IsNullOrEmpty(name) || IsReservedRootEntry(category) || name.StartsWith("."))
                    {
                        WriteError(res, ser, "子目录名称无效");
                        return;
                    }

                    lock (SyncLock)
                    {
                        string baseRoot = (zone == "inner") ? VaultDir : RootDir;
                        string subDir = Path.Combine(Path.Combine(baseRoot, category), name);
                        if (!IsAllowedVaultOrExternalPath(subDir) || IsProtectedSystemPath(subDir, false))
                        {
                            WriteError(res, ser, "越界拦截：子目录删除路径未通过院墙校验", 403);
                            return;
                        }
                        if (Directory.Exists(subDir) && Directory.GetFileSystemEntries(subDir).Length > 0)
                        {
                            WriteError(res, ser, "该子目录下还有藏品文件，请先将里面的文件移走或清空后再删除子目录！");
                            return;
                        }
                        if (Directory.Exists(subDir)) Directory.Delete(subDir, false);
                        Ledger.metaByKey.Remove(GetInternalKey(zone, category, name));
                        SaveLedger();
                    }

                    LogEvent("已删除空子目录: " + category + "\\" + name);
                    SyncAndGenerateCatalogs();
                    WriteOk(res, ser, "已删除空子目录「" + name + "」");
                    return;
                }

                if (path == "/api/subdir/toggle-mode" && req.HttpMethod == "POST")
                {
                    Dictionary<string, object> body = ReadJsonBody(req, ser);
                    string zone = GetStr(body, "zone") == "inner" ? "inner" : "outer";
                    string category = SanitizeSingleSegmentName(GetStr(body, "category"));
                    string name = SanitizeSingleSegmentName(GetStr(body, "name"));
                    string mode = GetStr(body, "mode") == "bundle" ? "bundle" : "subdir";

                    if (string.IsNullOrEmpty(category) || string.IsNullOrEmpty(name) || IsReservedRootEntry(category) || name.StartsWith("."))
                    {
                        WriteError(res, ser, "目录参数无效");
                        return;
                    }

                    string baseRoot = (zone == "inner") ? VaultDir : RootDir;
                    string subPath = Path.Combine(Path.Combine(baseRoot, category), name);
                    if (!IsAllowedVaultOrExternalPath(subPath) || IsProtectedSystemPath(subPath, false) || !Directory.Exists(subPath))
                    {
                        WriteError(res, ser, "目标子目录不存在或未通过安全校验", 403);
                        return;
                    }

                    lock (SyncLock)
                    {
                        string subKey = GetInternalKey(zone, category, name);
                        ItemMeta meta;
                        if (!Ledger.metaByKey.TryGetValue(subKey, out meta) || meta == null) meta = new ItemMeta();
                        meta.folderMode = mode;
                        meta.updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        Ledger.metaByKey[subKey] = meta;
                        SaveLedger();
                    }

                    SyncAndGenerateCatalogs();
                    WriteOk(res, ser, mode == "subdir" ? ("已将「" + name + "」展开为子目录分组") : ("已将「" + name + "」收起为单张工具箱卡片"));
                    return;
                }

                if (path == "/api/subdir/toggle-zone" && req.HttpMethod == "POST")
                {
                    Dictionary<string, object> body = ReadJsonBody(req, ser);
                    string curZone = GetStr(body, "zone") == "inner" ? "inner" : "outer";
                    string nextZone = curZone == "inner" ? "outer" : "inner";
                    string category = SanitizeSingleSegmentName(GetStr(body, "category"));
                    string name = SanitizeSingleSegmentName(GetStr(body, "name"));
                    string cleanSubName = SanitizeSingleSegmentName(StripLeadingUnderscores(name, true));

                    if (string.IsNullOrEmpty(category) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(cleanSubName) ||
                        IsReservedRootEntry(category) || name.StartsWith(".") || IsReservedRootEntry(cleanSubName))
                    {
                        WriteError(res, ser, "子目录参数无效");
                        return;
                    }

                    lock (SyncLock)
                    {
                        string srcBase = (curZone == "inner") ? VaultDir : RootDir;
                        string dstBase = (nextZone == "inner") ? VaultDir : RootDir;
                        string srcSub = Path.Combine(Path.Combine(srcBase, category), name);
                        string dstSub = Path.Combine(Path.Combine(dstBase, category), cleanSubName);
                        if (!IsAllowedVaultOrExternalPath(srcSub) || IsProtectedSystemPath(srcSub, false) ||
                            !IsAllowedVaultOrExternalPath(dstSub) || IsProtectedSystemPath(dstSub, false))
                        {
                            WriteError(res, ser, "安全护栏：子目录搬移路径未通过院墙校验", 403);
                            return;
                        }
                        if (!Directory.Exists(srcSub))
                        {
                            WriteError(res, ser, "源子目录不存在");
                            return;
                        }

                        string oldSubKey = GetInternalKey(curZone, category, name);
                        string newSubKey = GetInternalKey(nextZone, category, cleanSubName);
                        Directory.CreateDirectory(Path.GetDirectoryName(dstSub));
                        if (!Directory.Exists(dstSub))
                        {
                            Directory.Move(srcSub, dstSub);
                            MigrateLedgerKeyTree(oldSubKey, newSubKey, true);
                        }
                        else
                        {
                            MergeDirectoryAndMigrateKeys(srcSub, dstSub, oldSubKey, newSubKey, true);
                            if (Ledger.metaByKey.ContainsKey(oldSubKey))
                            {
                                if (!Ledger.metaByKey.ContainsKey(newSubKey)) Ledger.metaByKey[newSubKey] = Ledger.metaByKey[oldSubKey];
                                Ledger.metaByKey.Remove(oldSubKey);
                            }
                        }

                        ItemMeta subMeta;
                        if (!Ledger.metaByKey.TryGetValue(newSubKey, out subMeta) || subMeta == null) subMeta = new ItemMeta();
                        subMeta.folderMode = "subdir";
                        Ledger.metaByKey[newSubKey] = subMeta;
                        SaveLedger();
                    }

                    SyncAndGenerateCatalogs();
                    WriteOk(res, ser, string.Format("已将子目录「{0}」整组移至{1}", name, nextZone == "inner" ? "🌙里书架" : "☀️表书架"));
                    return;
                }

                if (path == "/api/category/create" && req.HttpMethod == "POST")
                {
                    Dictionary<string, object> body = ReadJsonBody(req, ser);
                    string rawName = SanitizeSingleSegmentName(StripLeadingUnderscores(GetStr(body, "name"), true));
                    if (string.IsNullOrEmpty(rawName))
                    {
                        WriteError(res, ser, "分类名称不能为空");
                        return;
                    }
                    if (IsReservedRootEntry(rawName))
                    {
                        WriteError(res, ser, "该名称为系统保留名称");
                        return;
                    }

                    string newOuterCat = Path.Combine(RootDir, rawName);
                    string newInnerCat = Path.Combine(VaultDir, rawName);
                    if (!IsAllowedVaultOrExternalPath(newOuterCat) || IsProtectedSystemPath(newOuterCat, false) ||
                        !IsAllowedVaultOrExternalPath(newInnerCat) || IsProtectedSystemPath(newInnerCat, false))
                    {
                        WriteError(res, ser, "越界拦截：分类路径未通过院墙校验", 403);
                        return;
                    }

                    lock (SyncLock)
                    {
                        Directory.CreateDirectory(newOuterCat);
                        Directory.CreateDirectory(newInnerCat);
                        if (!Ledger.categoryOrder.Contains(rawName))
                        {
                            Ledger.categoryOrder.Add(rawName);
                        }
                        SaveLedger();
                    }
                    LogEvent("新建分类架: " + rawName);
                    SyncAndGenerateCatalogs();
                    WriteOk(res, ser, "已创建分类「" + rawName + "」");
                    return;
                }

                if (path == "/api/category/rename" && req.HttpMethod == "POST")
                {
                    Dictionary<string, object> body = ReadJsonBody(req, ser);
                    string oldName = SanitizeSingleSegmentName(GetStr(body, "oldName"));
                    string newName = SanitizeSingleSegmentName(StripLeadingUnderscores(GetStr(body, "newName"), true));
                    if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName) || IsReservedRootEntry(oldName) || IsReservedRootEntry(newName))
                    {
                        WriteError(res, ser, "分类名称无效或属于系统保留名称");
                        return;
                    }
                    if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
                    {
                        WriteOk(res, ser, "名称未变更");
                        return;
                    }

                    lock (SyncLock)
                    {
                        string oldOuter = Path.Combine(RootDir, oldName);
                        string newOuter = Path.Combine(RootDir, newName);
                        string oldInner = Path.Combine(VaultDir, oldName);
                        string newInner = Path.Combine(VaultDir, newName);
                        if (!IsAllowedVaultOrExternalPath(oldOuter) || IsProtectedSystemPath(oldOuter, false) ||
                            !IsAllowedVaultOrExternalPath(newOuter) || IsProtectedSystemPath(newOuter, false) ||
                            !IsAllowedVaultOrExternalPath(oldInner) || IsProtectedSystemPath(oldInner, false) ||
                            !IsAllowedVaultOrExternalPath(newInner) || IsProtectedSystemPath(newInner, false))
                        {
                            WriteError(res, ser, "越界拦截：分类重命名路径未通过院墙校验", 403);
                            return;
                        }

                        Action<string, string, string> moveCatZone = delegate(string z, string oldDir, string newDir)
                        {
                            string oldCatPrefix = z + "::" + oldName + "::";
                            string newCatPrefix = z + "::" + newName + "::";
                            if (Directory.Exists(oldDir))
                            {
                                if (!Directory.Exists(newDir))
                                {
                                    Directory.Move(oldDir, newDir);
                                    List<string> catKeys = new List<string>();
                                    foreach (string k in Ledger.metaByKey.Keys)
                                    {
                                        if (k.StartsWith(oldCatPrefix, StringComparison.OrdinalIgnoreCase)) catKeys.Add(k);
                                    }
                                    foreach (string k in catKeys)
                                    {
                                        ItemMeta m = Ledger.metaByKey[k];
                                        Ledger.metaByKey.Remove(k);
                                        Ledger.metaByKey[newCatPrefix + k.Substring(oldCatPrefix.Length)] = m;
                                    }
                                }
                                else
                                {
                                    foreach (string f in Directory.GetFileSystemEntries(oldDir))
                                    {
                                        if (IsReparsePointPath(f)) continue;
                                        string origEntry = Path.GetFileName(f);
                                        bool ren;
                                        string moved = MovePhysicalEntrySafe(f, newDir, origEntry, out ren);
                                        string finalEntry = Path.GetFileName(moved);
                                        MigrateLedgerKeyTree(oldCatPrefix + origEntry, newCatPrefix + finalEntry, false);
                                    }
                                    if (Directory.GetFileSystemEntries(oldDir).Length == 0)
                                        Directory.Delete(oldDir, false);
                                }
                            }
                        };

                        moveCatZone("outer", oldOuter, newOuter);
                        moveCatZone("inner", oldInner, newInner);

                        for (int i = 0; i < Ledger.categoryOrder.Count; i++)
                        {
                            if (string.Equals(Ledger.categoryOrder[i], oldName, StringComparison.OrdinalIgnoreCase))
                            {
                                Ledger.categoryOrder[i] = newName;
                            }
                        }

                        foreach (ExternalItem ext in Ledger.externalItems)
                        {
                            if (string.Equals(ext.category, oldName, StringComparison.OrdinalIgnoreCase))
                            {
                                ext.category = newName;
                            }
                        }
                        SaveLedger();
                    }
                    LogEvent("分类重命名: " + oldName + " -> " + newName);
                    SyncAndGenerateCatalogs();
                    WriteOk(res, ser, "已重命名分类为「" + newName + "」");
                    return;
                }

                if (path == "/api/category/delete" && req.HttpMethod == "POST")
                {
                    Dictionary<string, object> body = ReadJsonBody(req, ser);
                    string name = SanitizeSingleSegmentName(GetStr(body, "name"));
                    if (string.IsNullOrEmpty(name) || IsReservedRootEntry(name))
                    {
                        WriteError(res, ser, "分类名称无效或属于系统保留名称");
                        return;
                    }
                    string fallbackCat = "未分类";

                    lock (SyncLock)
                    {
                        string outerDir = Path.Combine(RootDir, name);
                        string innerDir = Path.Combine(VaultDir, name);
                        if (!IsAllowedVaultOrExternalPath(outerDir) || IsProtectedSystemPath(outerDir, false) ||
                            !IsAllowedVaultOrExternalPath(innerDir) || IsProtectedSystemPath(innerDir, false))
                        {
                            WriteError(res, ser, "越界拦截：分类删除路径未通过院墙校验", 403);
                            return;
                        }

                        bool hasOuterFiles = Directory.Exists(outerDir) && Directory.GetFileSystemEntries(outerDir).Length > 0;
                        bool hasInnerFiles = Directory.Exists(innerDir) && Directory.GetFileSystemEntries(innerDir).Length > 0;

                        if (hasOuterFiles || hasInnerFiles)
                        {
                            // 为防止误删藏品，如果分类非空，将其安全转入「未分类」或提示用户
                            WriteError(res, ser, "该分类下还有实体文件，请先移走或清空里面的文件后再删除分类架！");
                            return;
                        }

                        if (Directory.Exists(outerDir)) Directory.Delete(outerDir, false);
                        if (Directory.Exists(innerDir)) Directory.Delete(innerDir, false);

                        Ledger.categoryOrder.RemoveAll(delegate(string c)
                        {
                            return string.Equals(c, name, StringComparison.OrdinalIgnoreCase);
                        });

                        // 外部挂牌项绝不删盘，仅将分类改到首个可用分类
                        if (Ledger.categoryOrder.Count > 0) fallbackCat = Ledger.categoryOrder[0];
                        foreach (ExternalItem ext in Ledger.externalItems)
                        {
                            if (string.Equals(ext.category, name, StringComparison.OrdinalIgnoreCase))
                            {
                                ext.category = fallbackCat;
                            }
                        }
                        SaveLedger();
                    }
                    LogEvent("已删除空分类架: " + name);
                    SyncAndGenerateCatalogs();
                    WriteOk(res, ser, "已删除分类「" + name + "」");
                    return;
                }

                if (path == "/api/item/toggle-zone" && req.HttpMethod == "POST")
                {
                    Dictionary<string, object> body = ReadJsonBody(req, ser);
                    string key = GetStr(body, "key");
                    string targetZone = GetStr(body, "targetZone"); // "outer" or "inner"

                    lock (SyncLock)
                    {
                        // 情况 A：外部原位挂牌项 -> 严禁移动或重命名原文件！仅改账本标记！
                        if (key.StartsWith("ext::"))
                        {
                            string extId = key.Substring(5);
                            ExternalItem found = Ledger.externalItems.Find(delegate(ExternalItem x) { return x.id == extId; });
                            if (found == null)
                            {
                                WriteError(res, ser, "未找到该外部挂牌项");
                                return;
                            }
                            string newZone = string.IsNullOrEmpty(targetZone)
                                ? (found.zone == "inner" ? "outer" : "inner")
                                : targetZone;
                            found.zone = newZone;
                            SaveLedger();
                            LogEvent(string.Format("外部原位挂牌项 [{0}] 切换至{1}（原文件位置与名称未改动）",
                                found.name, newZone == "inner" ? "🌙里书架" : "☀️表书架"));
                            SyncAndGenerateCatalogs();
                            WriteOk(res, ser, string.Format("已将外部挂牌「{0}」切入{1}（原盘路径保持不动）",
                                found.name, newZone == "inner" ? "🌙里书架" : "☀️表书架"));
                            return;
                        }

                        // 情况 B：内部实体文件/文件夹 -> 在 E:\Gold Mine\<cat>\ 与 E:\Gold Mine\.vault\<cat>\ 之间物理搬移（支持子目录内项目，带同名防覆盖保护）
                        string[] parts = key.Split(new string[] { "::" }, 3, StringSplitOptions.None);
                        if (parts.Length < 3)
                        {
                            WriteError(res, ser, "藏品标识无效");
                            return;
                        }
                        string curZone = parts[0] == "inner" ? "inner" : "outer";
                        string cat = SanitizeSingleSegmentName(parts[1]);
                        string relSubPath = SanitizeRelativeSubPath(parts[2]);
                        if (string.IsNullOrEmpty(cat) || string.IsNullOrEmpty(relSubPath) || IsReservedRootEntry(cat))
                        {
                            WriteError(res, ser, "藏品分类或名称无效");
                            return;
                        }
                        string nextZone = (targetZone == "inner" || targetZone == "outer")
                            ? targetZone
                            : (curZone == "inner" ? "outer" : "inner");

                        if (curZone == nextZone)
                        {
                            WriteOk(res, ser, "已处于目标书架");
                            return;
                        }

                        string relDir = Path.GetDirectoryName(relSubPath) ?? "";
                        string itemName = Path.GetFileName(relSubPath);
                        string srcCatDir = (curZone == "inner") ? Path.Combine(VaultDir, cat) : Path.Combine(RootDir, cat);
                        string dstCatDir = (nextZone == "inner") ? Path.Combine(VaultDir, cat) : Path.Combine(RootDir, cat);
                        string srcDir = string.IsNullOrEmpty(relDir) ? srcCatDir : Path.Combine(srcCatDir, relDir);
                        string dstDir = string.IsNullOrEmpty(relDir) ? dstCatDir : Path.Combine(dstCatDir, relDir);
                        string srcPath = Path.Combine(srcDir, itemName);
                        if (!IsAllowedVaultOrExternalPath(srcPath) || IsProtectedSystemPath(srcPath, false) ||
                            !IsAllowedVaultOrExternalPath(dstDir) || IsProtectedSystemPath(dstDir, false))
                        {
                            WriteError(res, ser, "安全护栏：搬移路径未通过院墙或自保锁校验", 403);
                            return;
                        }

                        if (!File.Exists(srcPath) && !Directory.Exists(srcPath))
                        {
                            WriteError(res, ser, "源路径不存在: " + srcPath);
                            return;
                        }

                        string desiredName = StripLeadingUnderscores(itemName, Directory.Exists(srcPath));
                        if (string.IsNullOrEmpty(desiredName)) desiredName = "未命名藏品";
                        bool wasRenamed;
                        string finalPath = MovePhysicalEntrySafe(srcPath, dstDir, desiredName, out wasRenamed);
                        string finalName = Path.GetFileName(finalPath);
                        string newRelSubPath = string.IsNullOrEmpty(relDir) ? finalName : (relDir + "\\" + finalName);

                        string newKey = GetInternalKey(nextZone, cat, newRelSubPath);
                        MigrateLedgerKeyTree(key, newKey, false);
                        if (!string.IsNullOrEmpty(relDir))
                        {
                            string topSub = relDir.Split(new char[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)[0];
                            string dstSubKey = GetInternalKey(nextZone, cat, topSub);
                            ItemMeta sm;
                            if (!Ledger.metaByKey.TryGetValue(dstSubKey, out sm) || sm == null) sm = new ItemMeta();
                            if (string.IsNullOrEmpty(sm.folderMode)) sm.folderMode = "subdir";
                            Ledger.metaByKey[dstSubKey] = sm;
                        }
                        SaveLedger();
                        string msg = string.Format("已将「{0}」物理搬移至{1}{2}",
                            itemName,
                            nextZone == "inner" ? "🌙里书架 (.vault)" : "☀️表书架",
                            wasRenamed ? ("（检测到同名文件，已自动防覆盖保护重命名为 " + finalName + "）") : "");
                        LogEvent(msg);
                        SyncAndGenerateCatalogs();
                        Dictionary<string, object> okObj = new Dictionary<string, object>();
                        okObj["ok"] = true;
                        okObj["message"] = msg;
                        okObj["newKey"] = newKey;
                        WriteJsonResponse(res, ser.Serialize(okObj));
                        return;
                    }
                }

                if (path == "/api/item/move-category" && req.HttpMethod == "POST")
                {
                    Dictionary<string, object> body = ReadJsonBody(req, ser);
                    string key = GetStr(body, "key");
                    string targetCategory = SanitizeSingleSegmentName(StripLeadingUnderscores(GetStr(body, "targetCategory"), true));
                    string targetSubDir = SanitizeSingleSegmentName(StripLeadingUnderscores(GetStr(body, "targetSubDir"), true));
                    if (string.IsNullOrEmpty(targetCategory) || IsReservedRootEntry(targetCategory))
                    {
                        WriteError(res, ser, "目标分类无效或属于系统保留名称");
                        return;
                    }
                    if (!string.IsNullOrEmpty(targetSubDir) && (targetSubDir.StartsWith(".") || IsReservedRootEntry(targetSubDir)))
                    {
                        WriteError(res, ser, "目标子目录无效或属于系统保留名称");
                        return;
                    }

                    lock (SyncLock)
                    {
                        if (key.StartsWith("ext::"))
                        {
                            string extId = key.Substring(5);
                            ExternalItem found = Ledger.externalItems.Find(delegate(ExternalItem x) { return x.id == extId; });
                            if (found == null)
                            {
                                WriteError(res, ser, "未找到该外部挂牌项");
                                return;
                            }
                            found.category = targetCategory;
                            if (!Ledger.categoryOrder.Contains(targetCategory)) Ledger.categoryOrder.Add(targetCategory);
                            SaveLedger();
                            SyncAndGenerateCatalogs();
                            WriteOk(res, ser, "已将外部挂牌「" + found.name + "」归入分类「" + targetCategory + "」");
                            return;
                        }

                        string[] parts = key.Split(new string[] { "::" }, 3, StringSplitOptions.None);
                        if (parts.Length < 3)
                        {
                            WriteError(res, ser, "藏品标识无效");
                            return;
                        }
                        string curZone = parts[0] == "inner" ? "inner" : "outer";
                        string curCat = SanitizeSingleSegmentName(parts[1]);
                        string relSubPath = SanitizeRelativeSubPath(parts[2]);
                        if (string.IsNullOrEmpty(curCat) || string.IsNullOrEmpty(relSubPath) || IsReservedRootEntry(curCat))
                        {
                            WriteError(res, ser, "藏品分类或名称无效");
                            return;
                        }

                        string[] relSegs = relSubPath.Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                        string curTopSub = relSegs.Length >= 2 ? relSegs[0] : "";
                        string curLeafSub = relSegs.Length >= 3 ? relSegs[1] : "";
                        string itemName = relSegs[relSegs.Length - 1];

                        if (string.Equals(curCat, targetCategory, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(curTopSub, targetSubDir ?? "", StringComparison.OrdinalIgnoreCase))
                        {
                            WriteOk(res, ser, "已在该目录位置下");
                            return;
                        }

                        string effectiveRelDir = string.IsNullOrEmpty(targetSubDir)
                            ? ""
                            : (!string.IsNullOrEmpty(curLeafSub) && string.Equals(curTopSub, targetSubDir, StringComparison.OrdinalIgnoreCase)
                                ? (targetSubDir + "\\" + curLeafSub)
                                : targetSubDir);

                        string baseRoot = (curZone == "inner") ? VaultDir : RootDir;
                        string srcPath = Path.Combine(Path.Combine(baseRoot, curCat), relSubPath);
                        string dstCatDir = Path.Combine(baseRoot, targetCategory);
                        string dstDir = string.IsNullOrEmpty(effectiveRelDir) ? dstCatDir : Path.Combine(dstCatDir, effectiveRelDir);
                        if (!IsAllowedVaultOrExternalPath(srcPath) || IsProtectedSystemPath(srcPath, false) ||
                            !IsAllowedVaultOrExternalPath(dstDir) || IsProtectedSystemPath(dstDir, false))
                        {
                            WriteError(res, ser, "安全护栏：移动路径未通过院墙或自保锁校验", 403);
                            return;
                        }
                        if (!File.Exists(srcPath) && !Directory.Exists(srcPath))
                        {
                            WriteError(res, ser, "源路径不存在: " + srcPath, 404);
                            return;
                        }
                        string srcFull = Path.GetFullPath(srcPath).TrimEnd('\\', '/');
                        string dstDirFull = Path.GetFullPath(dstDir).TrimEnd('\\', '/');
                        if (Directory.Exists(srcPath) &&
                            (string.Equals(dstDirFull, srcFull, StringComparison.OrdinalIgnoreCase) ||
                             dstDirFull.StartsWith(srcFull + "\\", StringComparison.OrdinalIgnoreCase)))
                        {
                            WriteError(res, ser, "不能将目录移动到其自身或子目录内部");
                            return;
                        }

                        bool wasRenamed;
                        string finalPath = MovePhysicalEntrySafe(srcPath, dstDir, itemName, out wasRenamed);
                        string finalName = Path.GetFileName(finalPath);
                        string newRelSubPath = string.IsNullOrEmpty(effectiveRelDir) ? finalName : (effectiveRelDir + "\\" + finalName);
                        string newKey = GetInternalKey(curZone, targetCategory, newRelSubPath);

                        MigrateLedgerKeyTree(key, newKey, false);
                        if (!string.IsNullOrEmpty(targetSubDir))
                        {
                            string subKey = GetInternalKey(curZone, targetCategory, targetSubDir);
                            ItemMeta sm;
                            if (!Ledger.metaByKey.TryGetValue(subKey, out sm) || sm == null) sm = new ItemMeta();
                            sm.folderMode = "subdir";
                            Ledger.metaByKey[subKey] = sm;
                        }
                        if (!Ledger.categoryOrder.Contains(targetCategory)) Ledger.categoryOrder.Add(targetCategory);
                        SaveLedger();
                        SyncAndGenerateCatalogs();
                        string dstLabel = string.IsNullOrEmpty(targetSubDir) ? targetCategory : (targetCategory + " / " + targetSubDir);
                        Dictionary<string, object> okObj = new Dictionary<string, object>();
                        okObj["ok"] = true;
                        okObj["message"] = string.Format("已移动至「{0}」{1}", dstLabel, wasRenamed ? ("（防覆盖重命名为 " + finalName + "）") : "");
                        okObj["newKey"] = newKey;
                        WriteJsonResponse(res, ser.Serialize(okObj));
                        return;
                    }
                }

                if (path == "/api/item/meta" && req.HttpMethod == "POST")
                {
                    Dictionary<string, object> body = ReadJsonBody(req, ser);
                    string key = GetStr(body, "key");
                    string note = GetStr(body, "note");
                    string entry = GetStr(body, "entry");
                    string tagsRaw = GetStr(body, "tags");

                    List<string> tags = new List<string>();
                    if (!string.IsNullOrEmpty(tagsRaw))
                    {
                        string[] split = tagsRaw.Split(new char[] { ',', '，', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string s in split)
                        {
                            string t = s.Trim();
                            if (!string.IsNullOrEmpty(t) && !tags.Contains(t)) tags.Add(t);
                        }
                    }

                    lock (SyncLock)
                    {
                        if (key.StartsWith("ext::"))
                        {
                            string extId = key.Substring(5);
                            ExternalItem found = Ledger.externalItems.Find(delegate(ExternalItem x) { return x.id == extId; });
                            if (found == null)
                            {
                                WriteError(res, ser, "未找到该外部挂牌项");
                                return;
                            }
                            string customName = GetStr(body, "name").Trim();
                            if (!string.IsNullOrEmpty(customName)) found.name = customName;
                            found.note = note;
                            found.entry = entry;
                            found.tags = tags;
                            SaveLedger();
                        }
                        else
                        {
                            string[] kp = key.Split(new string[] { "::" }, 3, StringSplitOptions.None);
                            if (kp.Length < 3 || (kp[0] != "outer" && kp[0] != "inner"))
                            {
                                WriteError(res, ser, "藏品标识无效");
                                return;
                            }
                            string kCat = SanitizeSingleSegmentName(kp[1]);
                            string kRel = SanitizeRelativeSubPath(kp[2]);
                            if (string.IsNullOrEmpty(kCat) || string.IsNullOrEmpty(kRel) || IsReservedRootEntry(kCat))
                            {
                                WriteError(res, ser, "藏品标识包含非法路径或保留名");
                                return;
                            }
                            string normKey = GetInternalKey(kp[0], kCat, kRel);
                            ItemMeta meta;
                            if (!Ledger.metaByKey.TryGetValue(normKey, out meta) || meta == null)
                            {
                                meta = new ItemMeta();
                            }
                            meta.note = note;
                            meta.entry = entry;
                            meta.tags = tags;
                            meta.updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                            Ledger.metaByKey[normKey] = meta;
                            SaveLedger();
                        }
                    }

                    SyncAndGenerateCatalogs();
                    WriteOk(res, ser, "已保存备注与目录索引信息");
                    return;
                }

                if (path == "/api/item/content" && req.HttpMethod == "GET")
                {
                    string targetPath = GetUtf8QueryParam(req, "path");
                    if (!IsAllowedVaultOrExternalPath(targetPath) || IsProtectedSystemPath(targetPath, true))
                    {
                        WriteError(res, ser, "越界拦截：仅允许读取藏宝阁院内或已挂牌登记的非系统文件", 403);
                        return;
                    }
                    if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
                    {
                        WriteError(res, ser, "文件不存在: " + targetPath);
                        return;
                    }
                    FileInfo cfi = new FileInfo(targetPath);
                    if (cfi.Length > 16L * 1024 * 1024)
                    {
                        WriteError(res, ser, "文件体积超过 16 MB，请使用本地编辑器打开", 413);
                        return;
                    }
                    string content = ReadTextSmart(targetPath);
                    Dictionary<string, object> resp = new Dictionary<string, object>();
                    resp["ok"] = true;
                    resp["content"] = content;
                    resp["path"] = targetPath;
                    WriteJsonResponse(res, ser.Serialize(resp));
                    return;
                }

                if (path == "/api/item/save-content" && req.HttpMethod == "POST")
                {
                    Dictionary<string, object> body = ReadJsonBody(req, ser);
                    string targetPath = GetStr(body, "path");
                    string content = GetStr(body, "content");
                    if (!IsAllowedVaultOrExternalPath(targetPath) || IsProtectedSystemPath(targetPath, false))
                    {
                        WriteError(res, ser, "越界拦截：禁止修改系统受保护文件或院外未登记文件", 403);
                        return;
                    }
                    if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
                    {
                        WriteError(res, ser, "目标文件不存在");
                        return;
                    }
                    File.WriteAllText(targetPath, content, new UTF8Encoding(false));
                    LogEvent("已在线保存文件修改: " + Path.GetFileName(targetPath));
                    WriteOk(res, ser, "已保存至原文件");
                    return;
                }

                if (path == "/api/item/create-text" && req.HttpMethod == "POST")
                {
                    Dictionary<string, object> body = ReadJsonBody(req, ser);
                    string category = SanitizeSingleSegmentName(StripLeadingUnderscores(GetStr(body, "category"), true));
                    string subDir = SanitizeSingleSegmentName(StripLeadingUnderscores(GetStr(body, "subDir"), true));
                    string zone = GetStr(body, "zone") == "inner" ? "inner" : "outer";
                    string fileName = SanitizeSingleSegmentName(StripLeadingUnderscores(GetStr(body, "fileName"), false));
                    string content = GetStr(body, "content");
                    string note = GetStr(body, "note");
                    string tagsRaw = GetStr(body, "tags");

                    if (string.IsNullOrEmpty(category)) category = "文本与产出";
                    if (IsReservedRootEntry(category))
                    {
                        WriteError(res, ser, "分类名称为系统保留名称");
                        return;
                    }
                    if (!string.IsNullOrEmpty(subDir) && (subDir.StartsWith(".") || IsReservedRootEntry(subDir)))
                    {
                        WriteError(res, ser, "子目录名称无效或为系统保留名称");
                        return;
                    }
                    if (string.IsNullOrEmpty(fileName) || fileName.StartsWith(".")) fileName = "新建文稿_" + DateTime.Now.ToString("MMdd_HHmm") + ".md";
                    if (string.IsNullOrEmpty(Path.GetExtension(fileName))) fileName += ".md";
                    if (IsReservedRootEntry(fileName))
                    {
                        WriteError(res, ser, "文件名为系统保留名称");
                        return;
                    }

                    string catDir = (zone == "inner") ? Path.Combine(VaultDir, category) : Path.Combine(RootDir, category);
                    string targetDir = string.IsNullOrEmpty(subDir) ? catDir : Path.Combine(catDir, subDir);
                    string candidateCheck = Path.Combine(targetDir, fileName);
                    if (!IsAllowedVaultOrExternalPath(targetDir) || IsProtectedSystemPath(targetDir, false) ||
                        !IsAllowedVaultOrExternalPath(candidateCheck) || IsProtectedSystemPath(candidateCheck, false))
                    {
                        WriteError(res, ser, "越界拦截：新建文稿路径未通过院墙校验", 403);
                        return;
                    }
                    bool wasRenamed;
                    string finalPath;
                    string finalName;

                    lock (SyncLock)
                    {
                        finalPath = GetCollisionSafePath(targetDir, fileName, out wasRenamed);
                        finalName = Path.GetFileName(finalPath);
                        File.WriteAllText(finalPath, content ?? "", new UTF8Encoding(false));

                        if (!string.IsNullOrEmpty(subDir))
                        {
                            string subKey = GetInternalKey(zone, category, subDir);
                            ItemMeta sm;
                            if (!Ledger.metaByKey.TryGetValue(subKey, out sm) || sm == null) sm = new ItemMeta();
                            sm.folderMode = "subdir";
                            Ledger.metaByKey[subKey] = sm;
                        }

                        string relKeyName = string.IsNullOrEmpty(subDir) ? finalName : (subDir + "\\" + finalName);
                        string key = GetInternalKey(zone, category, relKeyName);
                        ItemMeta meta = new ItemMeta();
                        meta.note = note ?? "";
                        if (!string.IsNullOrEmpty(tagsRaw))
                        {
                            string[] split = tagsRaw.Split(new char[] { ',', '，', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (string s in split) meta.tags.Add(s.Trim());
                        }
                        Ledger.metaByKey[key] = meta;
                        if (!Ledger.categoryOrder.Contains(category)) Ledger.categoryOrder.Add(category);
                        SaveLedger();
                    }

                    string locLabel = string.IsNullOrEmpty(subDir) ? finalName : (subDir + "\\" + finalName);
                    LogEvent("新建文本入阁: " + locLabel + " (" + (zone == "inner" ? "里书架" : "表书架") + ")");
                    SyncAndGenerateCatalogs();
                    WriteOk(res, ser, "已存入" + (zone == "inner" ? "🌙里书架" : "☀️表书架") + "：" + locLabel);
                    return;
                }

                if (path == "/api/external/add" && req.HttpMethod == "POST")
                {
                    Dictionary<string, object> body = ReadJsonBody(req, ser);
                    string extPath = GetStr(body, "externalPath").Trim().Trim('"');
                    string name = GetStr(body, "name").Trim();
                    string category = StripLeadingUnderscores(GetStr(body, "category").Trim());
                    string zone = GetStr(body, "zone") == "inner" ? "inner" : "outer";
                    string note = GetStr(body, "note").Trim();
                    string entry = GetStr(body, "entry").Trim();
                    string tagsRaw = GetStr(body, "tags");

                    if (string.IsNullOrEmpty(extPath))
                    {
                        WriteError(res, ser, "外部路径不能为空");
                        return;
                    }
                    if (!File.Exists(extPath) && !Directory.Exists(extPath))
                    {
                        WriteError(res, ser, "找不到该路径，请检查路径是否正确: " + extPath);
                        return;
                    }
                    if (IsReparsePointPath(extPath) || IsProtectedSystemPath(extPath, false))
                    {
                        WriteError(res, ser, "安全护栏：禁止将软链接或曜蚀阁内部系统文件登记为外部挂牌项", 403);
                        return;
                    }
                    string realExtAdd = ResolveRealPath(extPath);
                    string realRootAdd = ResolveRealPath(RootDir);
                    string realVaultAdd = ResolveRealPath(VaultDir);
                    if (string.Equals(realExtAdd, realRootAdd, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(realExtAdd, realVaultAdd, StringComparison.OrdinalIgnoreCase) ||
                        IsProtectedSystemPath(realExtAdd, false))
                    {
                        WriteError(res, ser, "安全护栏：禁止将曜蚀阁根目录或系统仓登记为外部挂牌项", 403);
                        return;
                    }
                    category = SanitizeSingleSegmentName(StripLeadingUnderscores(category, true));
                    if (string.IsNullOrEmpty(category) || IsReservedRootEntry(category)) category = "工具与插件";
                    if (string.IsNullOrEmpty(name))
                    {
                        name = Path.GetFileName(extPath.TrimEnd('\\', '/'));
                        if (string.IsNullOrEmpty(name)) name = extPath;
                    }

                    lock (SyncLock)
                    {
                        ExternalItem ext = new ExternalItem();
                        ext.name = name;
                        ext.externalPath = extPath;
                        ext.category = category;
                        ext.zone = zone;
                        ext.note = note;
                        ext.entry = entry;
                        if (!string.IsNullOrEmpty(tagsRaw))
                        {
                            string[] split = tagsRaw.Split(new char[] { ',', '，', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (string s in split) ext.tags.Add(s.Trim());
                        }
                        Ledger.externalItems.Add(ext);
                        if (!Ledger.categoryOrder.Contains(category)) Ledger.categoryOrder.Add(category);
                        SaveLedger();
                    }

                    LogEvent("挂载外部原位项目: " + name + " (" + extPath + ")");
                    SyncAndGenerateCatalogs();
                    WriteOk(res, ser, "已原位挂牌「" + name + "」（原文件位置绝不改动）");
                    return;
                }

                if (path == "/api/external/remove" && req.HttpMethod == "POST")
                {
                    Dictionary<string, object> body = ReadJsonBody(req, ser);
                    string key = GetStr(body, "key");
                    if (!key.StartsWith("ext::"))
                    {
                        WriteError(res, ser, "仅外部挂牌项可执行摘牌操作");
                        return;
                    }
                    string extId = key.Substring(5);
                    lock (SyncLock)
                    {
                        Ledger.externalItems.RemoveAll(delegate(ExternalItem x) { return x.id == extId; });
                        SaveLedger();
                    }
                    LogEvent("已摘除外部挂牌项 (原文件完好无损): " + extId);
                    SyncAndGenerateCatalogs();
                    WriteOk(res, ser, "已从书架摘牌（外部原文件完好保留）");
                    return;
                }

                if (path == "/api/dialog/pick" && req.HttpMethod == "POST")
                {
                    Dictionary<string, object> body = ReadJsonBody(req, ser);
                    string mode = GetStr(body, "mode"); // "folder" or "file"
                    if (Interlocked.CompareExchange(ref DialogPickBusy, 1, 0) != 0)
                    {
                        WriteError(res, ser, "已有文件选择窗口打开中，请先完成或关闭当前选择窗口", 409);
                        return;
                    }
                    string picked = "";
                    try
                    {
                        Thread staThread = new Thread(delegate()
                        {
                            using (Form dummy = new Form())
                            {
                                dummy.TopMost = true;
                                dummy.ShowInTaskbar = false;
                                dummy.FormBorderStyle = FormBorderStyle.None;
                                dummy.StartPosition = FormStartPosition.Manual;
                                dummy.Location = new Point(-2000, -2000);
                                dummy.Size = new Size(1, 1);
                                dummy.Show();
                                ForceWindowToForeground(dummy.Handle);
                                dummy.Activate();

                                if (mode == "file")
                                {
                                    using (OpenFileDialog ofd = new OpenFileDialog())
                                    {
                                        ofd.Title = "选择要原位挂牌到曜蚀阁的外部文件";
                                        ofd.Filter = "所有文件 (*.*)|*.*";
                                        if (ofd.ShowDialog(dummy) == DialogResult.OK)
                                        {
                                            picked = ofd.FileName;
                                        }
                                    }
                                }
                                else
                                {
                                    using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                                    {
                                        fbd.Description = "选择要原位挂牌到曜蚀阁的外部目录（如 DSH、酒馆、Galgame 工具目录等，只挂号不搬家）";
                                        if (fbd.ShowDialog(dummy) == DialogResult.OK)
                                        {
                                            picked = fbd.SelectedPath;
                                        }
                                    }
                                }
                            }
                        });
                        staThread.SetApartmentState(ApartmentState.STA);
                        staThread.Start();
                        staThread.Join();
                    }
                    finally
                    {
                        Interlocked.Exchange(ref DialogPickBusy, 0);
                    }

                    Dictionary<string, object> resp = new Dictionary<string, object>();
                    resp["ok"] = true;
                    resp["pickedPath"] = picked;
                    WriteJsonResponse(res, ser.Serialize(resp));
                    return;
                }

                if (path == "/api/action/open-explorer" && req.HttpMethod == "POST")
                {
                    Dictionary<string, object> body = ReadJsonBody(req, ser);
                    string targetPath = GetStr(body, "path");
                    if (string.IsNullOrEmpty(targetPath))
                    {
                        WriteError(res, ser, "路径为空");
                        return;
                    }
                    if (!IsAllowedVaultOrExternalPath(targetPath))
                    {
                        WriteError(res, ser, "越界拦截：仅允许打开藏宝阁院内或已挂牌登记的路径", 403);
                        return;
                    }
                    if (File.Exists(targetPath))
                    {
                        OpenExplorerAndPopToFront(targetPath, true);
                        WriteOk(res, ser, "已置顶弹出文件所在文件夹");
                        return;
                    }
                    else if (Directory.Exists(targetPath))
                    {
                        OpenExplorerAndPopToFront(targetPath, false);
                        WriteOk(res, ser, "已置顶弹出文件夹");
                        return;
                    }
                    else
                    {
                        WriteError(res, ser, "路径不存在: " + targetPath);
                        return;
                    }
                }

                if (path == "/api/action/run" && req.HttpMethod == "POST")
                {
                    Dictionary<string, object> body = ReadJsonBody(req, ser);
                    string basePath = GetStr(body, "path");
                    string entry = GetStr(body, "entry");

                    if (!IsAllowedVaultOrExternalPath(basePath))
                    {
                        WriteError(res, ser, "越界拦截：仅允许启动藏宝阁院内或已挂牌登记的程序", 403);
                        return;
                    }

                    string runTarget = basePath;
                    string workDir = Path.GetDirectoryName(basePath);

                    if (Directory.Exists(basePath))
                    {
                        workDir = basePath;
                        if (!string.IsNullOrEmpty(entry))
                        {
                            string baseFull = Path.GetFullPath(basePath).TrimEnd('\\', '/');
                            string candidate = Path.GetFullPath(Path.Combine(basePath, entry)).TrimEnd('\\', '/');
                            if (!candidate.StartsWith(baseFull + "\\", StringComparison.OrdinalIgnoreCase))
                            {
                                WriteError(res, ser, "越界拦截：启动入口试图逃逸目标目录", 403);
                                return;
                            }
                            if (File.Exists(candidate))
                            {
                                runTarget = candidate;
                                workDir = Path.GetDirectoryName(candidate);
                            }
                        }
                    }

                    if (!IsAllowedVaultOrExternalPath(runTarget) || IsProtectedSystemPath(runTarget, false))
                    {
                        WriteError(res, ser, "越界拦截：启动目标超出藏宝阁允许范围或属于受保护系统文件", 403);
                        return;
                    }

                    if (!File.Exists(runTarget) && !Directory.Exists(runTarget))
                    {
                        WriteError(res, ser, "找不到可启动的目标: " + runTarget);
                        return;
                    }

                    if (Directory.Exists(runTarget))
                    {
                        OpenExplorerAndPopToFront(runTarget, false);
                        WriteOk(res, ser, "已置顶打开目录: " + Path.GetFileName(runTarget));
                        return;
                    }

                    AllowSetForegroundWindow(-1);
                    keybd_event(0x12, 0, 0x0002, UIntPtr.Zero);
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = runTarget;
                    psi.WorkingDirectory = workDir ?? RootDir;
                    psi.UseShellExecute = true;
                    Process p = Process.Start(psi);
                    if (p != null)
                    {
                        ThreadPool.QueueUserWorkItem(delegate(object st)
                        {
                            for (int i = 0; i < 15; i++)
                            {
                                Thread.Sleep(60);
                                try
                                {
                                    p.Refresh();
                                    if (p.MainWindowHandle != IntPtr.Zero)
                                    {
                                        ForceWindowToForeground(p.MainWindowHandle);
                                        break;
                                    }
                                }
                                catch { break; }
                            }
                        });
                    }

                    LogEvent("启动程序/入口: " + Path.GetFileName(runTarget));
                    WriteOk(res, ser, "已启动: " + Path.GetFileName(runTarget));
                    return;
                }

                if (path == "/api/raw/ticket" && req.HttpMethod == "POST")
                {
                    Dictionary<string, object> body = ReadJsonBody(req, ser);
                    string filePath = GetStr(body, "path");
                    if (!IsAllowedVaultOrExternalPath(filePath) || IsProtectedSystemPath(filePath, true))
                    {
                        WriteError(res, ser, "越界拦截：仅允许为院内或已挂牌的非系统文件签发预览票", 403);
                        return;
                    }
                    if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                    {
                        WriteError(res, ser, "文件不存在，无法签发预览票", 404);
                        return;
                    }
                    string ticket = IssuePreviewTicket(filePath);
                    Dictionary<string, object> resp = new Dictionary<string, object>();
                    resp["ok"] = true;
                    resp["ticket"] = ticket;
                    resp["url"] = "/api/raw?t=" + ticket;
                    WriteJsonResponse(res, ser.Serialize(resp));
                    return;
                }

                WriteError(res, ser, "未知接口: " + path, 404);
            }
            catch (Exception ex)
            {
                WriteError(res, ser, "服务器处理异常: " + ex.Message, 500);
            }
        }

        private static string ReadTextSmart(string filePath)
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }
            try
            {
                UTF8Encoding utf8Strict = new UTF8Encoding(false, true);
                return utf8Strict.GetString(bytes);
            }
            catch
            {
                return Encoding.Default.GetString(bytes);
            }
        }

        private static void ServeStaticFile(HttpListenerResponse res, string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            string mime = "application/octet-stream";
            if (ext == ".html" || ext == ".htm")
            {
                mime = "text/html; charset=utf-8";
                res.Headers["Content-Security-Policy"] = "sandbox allow-scripts allow-forms allow-downloads allow-modals;";
                res.Headers["Referrer-Policy"] = "no-referrer";
            }
            else if (ext == ".md" || ext == ".txt" || ext == ".log" || ext == ".list") mime = "text/plain; charset=utf-8";
            else if (ext == ".json") mime = "application/json; charset=utf-8";
            else if (ext == ".png") mime = "image/png";
            else if (ext == ".jpg" || ext == ".jpeg") mime = "image/jpeg";
            else if (ext == ".gif") mime = "image/gif";
            else if (ext == ".webp") mime = "image/webp";
            else if (ext == ".svg")
            {
                mime = "image/svg+xml";
                res.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; img-src data:; sandbox;";
            }
            else if (ext == ".mp4") mime = "video/mp4";
            else if (ext == ".webm") mime = "video/webm";
            else if (ext == ".mp3") mime = "audio/mpeg";
            else if (ext == ".wav") mime = "audio/wav";
            else if (ext == ".ogg") mime = "audio/ogg";

            res.ContentType = mime;
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                res.ContentLength64 = fs.Length;
                byte[] buffer = new byte[65536];
                int read;
                while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                {
                    res.OutputStream.Write(buffer, 0, read);
                }
            }
            res.Close();
        }

        private static Dictionary<string, object> ReadJsonBody(HttpListenerRequest req, JavaScriptSerializer ser)
        {
            const long maxBytes = 32L * 1024 * 1024;
            if (req.ContentLength64 > maxBytes)
            {
                throw new InvalidOperationException("请求体超过 32 MB 限制");
            }
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] buf = new byte[16384];
                int read;
                long total = 0;
                while ((read = req.InputStream.Read(buf, 0, buf.Length)) > 0)
                {
                    total += read;
                    if (total > maxBytes) throw new InvalidOperationException("请求体超过 32 MB 限制");
                    ms.Write(buf, 0, read);
                }
                if (ms.Length == 0) return new Dictionary<string, object>();
                string raw = Encoding.UTF8.GetString(ms.ToArray());
                if (string.IsNullOrEmpty(raw)) return new Dictionary<string, object>();
                Dictionary<string, object> dict = ser.Deserialize<Dictionary<string, object>>(raw);
                return dict ?? new Dictionary<string, object>();
            }
        }

        private static string GetStr(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.ContainsKey(key) || dict[key] == null) return "";
            return Convert.ToString(dict[key]);
        }

        private static void WriteOk(HttpListenerResponse res, JavaScriptSerializer ser, string message)
        {
            Dictionary<string, object> obj = new Dictionary<string, object>();
            obj["ok"] = true;
            obj["message"] = message;
            WriteJsonResponse(res, ser.Serialize(obj));
        }

        private static void WriteError(HttpListenerResponse res, JavaScriptSerializer ser, string error, int code = 400)
        {
            res.StatusCode = code;
            Dictionary<string, object> obj = new Dictionary<string, object>();
            obj["ok"] = false;
            obj["error"] = error;
            WriteJsonResponse(res, ser.Serialize(obj));
        }

        private static void WriteJsonResponse(HttpListenerResponse res, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            res.ContentType = "application/json; charset=utf-8";
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.Close();
        }

        private static void WriteTextResponse(HttpListenerResponse res, string text, string contentType, int status = 200)
        {
            res.StatusCode = status;
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            res.ContentType = contentType;
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.Close();
        }
    }
}
