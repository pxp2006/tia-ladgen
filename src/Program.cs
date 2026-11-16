using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Online;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.OpcUa;

internal static class Program
{
    private static string opennessFolder;

    static Program()
    {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
    }

    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        AssemblyName requested = new AssemblyName(args.Name);
        if (requested.Name == null || !requested.Name.StartsWith("Siemens.Engineering", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        string candidate = Path.Combine(opennessFolder, requested.Name + ".dll");
        if (!File.Exists(candidate))
        {
            return null;
        }
        return Assembly.LoadFrom(candidate);
    }

    private static string FindOpennessFolder()
    {
        List<string> candidates = new List<string>();
        string env = Environment.GetEnvironmentVariable("TIA_INSTALLPATH");
        if (!string.IsNullOrEmpty(env))
        {
            candidates.Add(Path.Combine(Path.Combine(env, "PublicAPI"), Path.Combine("V21", "net48")));
        }
        candidates.Add(@"D:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48");
        candidates.Add(@"C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48");
        for (int i = 0; i < candidates.Count; i++)
        {
            if (Directory.Exists(candidates[i]))
            {
                return candidates[i];
            }
        }
        throw new DirectoryNotFoundException("Openness net48 folder not found.");
    }

    private static int Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "TiaProbe");
        try
        {
            Directory.CreateDirectory(outDir);
            TextWriter consoleOut = Console.Out;
            StreamWriter log = new StreamWriter(Path.Combine(outDir, "probe.log"), false, System.Text.Encoding.UTF8);
            log.AutoFlush = true;
            Console.SetOut(new TeeWriter(consoleOut, log));
        }
        catch (Exception)
        {
        }
        try
        {
            opennessFolder = FindOpennessFolder();
            Console.WriteLine("[OK] Openness folder: " + opennessFolder);
            string mode = args.Length > 1 ? args[1] : "";
            Probe probe = new Probe(outDir, mode);
            probe.Run();
            Console.WriteLine("[DONE]");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FAIL] " + ex.GetType().FullName);
            Console.WriteLine(ex.ToString());
            return 1;
        }
    }
}

internal sealed class TeeWriter : TextWriter
{
    private readonly TextWriter primary;
    private readonly TextWriter secondary;

    public TeeWriter(TextWriter primary, TextWriter secondary)
    {
        this.primary = primary;
        this.secondary = secondary;
    }

    public override System.Text.Encoding Encoding
    {
        get { return primary.Encoding; }
    }

    public override void Write(char value)
    {
        primary.Write(value);
        secondary.Write(value);
    }

    public override void Write(string value)
    {
        primary.Write(value);
        secondary.Write(value);
    }

    public override void WriteLine(string value)
    {
        primary.WriteLine(value);
        secondary.WriteLine(value);
    }

    public override void Flush()
    {
        primary.Flush();
        secondary.Flush();
    }
}

internal sealed class Probe
{
    private readonly string outDir;
    private readonly string mode;
    private TiaPortal tia;
    private bool attached;

    public Probe(string outDir, string mode)
    {
        this.outDir = outDir;
        this.mode = mode;
    }

    public void Run()
    {
        DirectoryInfo root = new DirectoryInfo(outDir);
        if (!root.Exists)
        {
            root.Create();
        }

        if (!Connect())
        {
            return;
        }
        try
        {
            DumpOpenProjects();
            if (mode == "--create")
            {
                CreateScratchProjectWithLadBlock(root);
            }
            if (mode == "--skeleton")
            {
                CreateScratchProjectWithLadBlock(root);
            }
            if (mode == "--skeleton-live")
            {
                SkeletonFromOpenProject();
            }
            if (mode == "--diag")
            {
                Diagnostics();
            }
            if (mode == "--import")
            {
                ImportTest();
            }
            if (mode == "--cleanup")
            {
                CleanupTestBlocks();
            }
            if (mode == "--list")
            {
                ListBlocks();
            }
            if (mode == "--findcompilable")
            {
                FindCompilableTypes();
            }
            if (mode == "--verify")
            {
                CleanupTestBlocks();
                ImportTest();
                ListBlocks();
                FindCompilableTypes();
            }
            if (mode == "--gen")
            {
                GenFromCsv();
            }
            if (mode == "--services")
            {
                Services();
            }
            if (mode == "--timer")
            {
                TimerTest();
            }
            if (mode == "--maintags")
            {
                CreateMainTags();
            }
            if (mode == "--mkidb")
            {
                MakeInstanceDb();
            }
            if (mode == "--offline")
            {
                GoOfflineAll();
            }
            if (mode == "--reset")
            {
                ResetDemo();
            }
            if (mode == "--onstate")
            {
                OnlineStateReport();
            }
            if (mode == "--audit")
            {
                Audit();
            }
            if (mode == "--addtag")
            {
                AddTag("心跳", "M32.0");
            }
            if (mode == "--opc")
            {
                OpcAudit();
            }
            if (mode == "--export")
            {
                ExportAllBlocks();
            }
        }
        finally
        {
            if (!attached)
            {
                tia.Dispose();
                Console.WriteLine("[..] TIA Portal disposed");
            }
            else
            {
                Console.WriteLine("[..] attached instance left running (not disposed)");
            }
        }
    }

    private bool Connect()
    {
        Console.WriteLine("[DBG] user=" + Environment.UserName + " session=" + Process.GetCurrentProcess().SessionId + " totalProcs=" + Process.GetProcesses().Length);
        Console.WriteLine("[DBG] byName=" + Process.GetProcessesByName("Siemens.Automation.Portal").Length + " is64=" + Environment.Is64BitProcess);
        List<TiaPortalProcess> running = new List<TiaPortalProcess>(TiaPortal.GetProcesses());
        Console.WriteLine("[INFO] running TIA instances: " + running.Count);
        for (int i = 0; i < running.Count; i++)
        {
            TiaPortalProcess p = running[i];
            string proj = "";
            try
            {
                proj = p.ProjectPath == null ? "" : p.ProjectPath.FullName;
            }
            catch (Exception)
            {
            }
            Console.WriteLine("[INFO] instance " + i + " id=" + p.Id + " mode=" + p.Mode + " project=" + proj);
        }
        if (running.Count > 0)
        {
            try
            {
                tia = running[0].Attach();
                attached = true;
                Console.WriteLine("[OK] attached to running instance");
                Console.WriteLine("[INFO] API version: " + typeof(TiaPortal).Assembly.GetName().Version);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FAIL] attach: " + ex.GetType().Name + " :: " + FirstLine(ex.Message));
                return false;
            }
        }

        Console.WriteLine("[..] GetProcesses returned nothing, trying PID lookup on Siemens.Automation.Portal");
        Process[] byName = Process.GetProcessesByName("Siemens.Automation.Portal");
        for (int i = 0; i < byName.Length; i++)
        {
            Process p = byName[i];
            try
            {
                Console.WriteLine("[INFO] try pid=" + p.Id + " window='" + p.MainWindowTitle + "'");
                TiaPortalProcess tp = TiaPortal.GetProcess(p.Id, 30000);
                if (tp == null)
                {
                    Console.WriteLine("[MISS] GetProcess returned null for " + p.Id);
                    continue;
                }
                tia = tp.Attach();
                attached = true;
                Console.WriteLine("[OK] attached via pid " + p.Id);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[MISS] pid " + p.Id + ": " + ex.GetType().Name + " :: " + FirstLine(ex.Message));
            }
        }
        try
        {
            Console.WriteLine("[..] no instance running, starting headless TIA Portal (1-3 min)");
            tia = new TiaPortal(TiaPortalMode.WithoutUserInterface);
            Console.WriteLine("[OK] headless instance started");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FAIL] start: " + ex.GetType().Name + " :: " + FirstLine(ex.Message));
            return false;
        }
    }

    private void DumpOpenProjects()
    {
        foreach (Project project in tia.Projects)
        {
            Console.WriteLine("[PROJECT] " + project.Name + " | " + project.Path);
            int count = 0;
            foreach (Device device in project.Devices)
            {
                count++;
                DumpDevice(device);
            }
            Console.WriteLine("[PROJECT] devices=" + count);
        }
    }

    private void CreateScratchProjectWithLadBlock(DirectoryInfo root)
    {
        DirectoryInfo projectDir = new DirectoryInfo(Path.Combine(root.FullName, "DemoProj"));
        if (projectDir.Exists)
        {
            Console.WriteLine("[WARN] scratch project already exists, skip create: " + projectDir.FullName);
            return;
        }
        Project project = tia.Projects.Create(projectDir, "DemoProj");
        Console.WriteLine("[OK] scratch project created: " + project.Path);
        Device device = TryCreateDevice(project);
        if (device == null)
        {
            Console.WriteLine("[WARN] device creation failed");
            return;
        }
        DumpDevice(device);
        ExportLadSkeleton(device);
    }

    private Device TryCreateDevice(Project project)
    {
        string[] mlfbs = new string[]
        {
            "6ES7 214-1AG40-0XB0",
            "6ES7 215-1AG40-0XB0",
            "6ES7 516-3AN02-0AB0",
            "6ES7 515-2AM02-0AB0"
        };
        for (int i = 0; i < mlfbs.Length; i++)
        {
            try
            {
                Device dev = project.Devices.CreateWithItem(mlfbs[i], "PLC_1", "PLC_1");
                Console.WriteLine("[OK] device created from " + mlfbs[i]);
                return dev;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[MISS] " + mlfbs[i] + " -> " + ex.GetType().Name + " :: " + FirstLine(ex.Message));
            }
        }
        return null;
    }

    private void DumpDevice(Device device)
    {
        string deviceType = "";
        try
        {
            deviceType = device.TypeIdentifier;
        }
        catch (Exception)
        {
        }
        Console.WriteLine("[DEVICE] " + device.Name + " | " + deviceType);
        DumpItems(device.DeviceItems, 1);
    }

    private void DumpItems(DeviceItemComposition items, int depth)
    {
        if (items == null)
        {
            return;
        }
        string pad = new string('-', depth * 2) + " ";
        foreach (DeviceItem item in items)
        {
            string typeId = "";
            string orderNo = "";
            string fw = "";
            try
            {
                typeId = item.TypeIdentifier;
            }
            catch (Exception)
            {
            }
            try
            {
                orderNo = Attr(item, "OrderNumber");
            }
            catch (Exception)
            {
            }
            try
            {
                fw = Attr(item, "FirmwareVersion");
            }
            catch (Exception)
            {
            }
            Console.WriteLine("[ITEM] " + pad + item.Name + " | " + typeId + " | MLFB=" + orderNo + " | FW=" + fw);
            DumpItems(item.DeviceItems, depth + 1);
        }
    }

    private static string Attr(DeviceItem item, string name)
    {
        object value = ((IEngineeringObject)item).GetAttribute(name);
        return value == null ? "" : value.ToString();
    }

    private void ExportLadSkeleton(Device device)
    {
        PlcSoftware software = GetPlcSoftware(device.DeviceItems);
        if (software == null)
        {
            Console.WriteLine("[WARN] PlcSoftware not available");
            return;
        }
        Console.WriteLine("[OK] PlcSoftware: " + software.Name);
        try
        {
            FB fb = software.BlockGroup.Blocks.CreateFB("FB_LadSkeleton", false, 1, ProgrammingLanguage.LAD);
            Console.WriteLine("[OK] LAD FB created: " + fb.Name + " lang=" + fb.ProgrammingLanguage);
            string xmlPath = Path.Combine(outDir, "FB_LadSkeleton_export.xml");
            fb.Export(new FileInfo(xmlPath), ExportOptions.None);
            Console.WriteLine("[OK] exported: " + xmlPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FAIL] create/export block: " + ex.GetType().Name + " :: " + FirstLine(ex.Message));
        }
    }

    private void ListBlocks()
    {
        foreach (Project project in tia.Projects)
        {
            foreach (Device device in project.Devices)
            {
                PlcSoftware software = GetPlcSoftware(device.DeviceItems);
                if (software == null)
                {
                    continue;
                }
                foreach (PlcBlock block in software.BlockGroup.Blocks)
                {
                    Console.WriteLine("[BLOCK] " + block.Name + " lang=" + block.ProgrammingLanguage + " number=" + block.Number);
                }
            }
        }
    }

    private static void FindCompilableTypes()
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int a = 0; a < assemblies.Length; a++)
        {
            Assembly assembly = assemblies[a];
            if (!assembly.GetName().Name.StartsWith("Siemens.Engineering"))
            {
                continue;
            }
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (Exception)
            {
                continue;
            }
            for (int t = 0; t < types.Length; t++)
            {
                if (types[t].IsClass && !types[t].IsAbstract && typeof(ICompilable).IsAssignableFrom(types[t]))
                {
                    Console.WriteLine("[COMPILABLE] " + types[t].FullName + " in " + assembly.GetName().Name);
                }
            }
        }
    }

    private static void PrintMessageTree(CompilerResultMessage message, int depth)
    {
        string pad = new string(' ', depth * 2);
        Console.WriteLine("[IDB-MSG] " + pad + message.State + " | path=" + message.Path + " | " + message.Description);
        try
        {
            foreach (CompilerResultMessage sub in message.Messages)
            {
                PrintMessageTree(sub, depth + 1);
            }
        }
        catch (Exception)
        {
        }
    }

    private void OpcAudit()
    {
        foreach (Project project in tia.Projects)
        {
            foreach (Device device in project.Devices)
            {
                PlcSoftware software = GetPlcSoftware(device.DeviceItems);
                if (software == null)
                {
                    continue;
                }
                IEngineeringServiceProvider sp = software as IEngineeringServiceProvider;
                if (sp == null)
                {
                    Console.WriteLine("[OPC] no service provider");
                    continue;
                }
                try
                {
                    OpcUaProvider provider = sp.GetService<OpcUaProvider>();
                    if (provider == null)
                    {
                        Console.WriteLine("[OPC] OpcUaProvider = null");
                    }
                    else
                    {
                        Console.WriteLine("[OPC] provider ok, communicationGroup=" + (provider.CommunicationGroup == null ? "null" : provider.CommunicationGroup.ToString()));
                        OpcUaCommunicationGroup group = provider.CommunicationGroup;
                        if (group != null && group.ServerInterfaceGroup != null)
                        {
                            ServerInterfaceGroup sig = group.ServerInterfaceGroup;
                            Console.WriteLine("[OPC] ServerInterfaces=" + sig.ServerInterfaces.Count);
                            foreach (ServerInterface si in sig.ServerInterfaces)
                            {
                                Console.WriteLine("[OPC-SI] " + si.Name + " enabled=" + si.Enabled);
                            }
                            Console.WriteLine("[OPC] SimaticInterfaces=" + sig.SimaticInterfaces.Count);
                            foreach (SimaticInterface sim in sig.SimaticInterfaces)
                            {
                                Console.WriteLine("[OPC-SIM] " + sim.Name + " enabled=" + sim.Enabled);
                            }
                            Console.WriteLine("[OPC] ReferenceNamespaces=" + sig.ReferenceNamespaces.Count);
                            foreach (ReferenceNamespace rn in sig.ReferenceNamespaces)
                            {
                                Console.WriteLine("[OPC-RN] " + rn.Name + " enabled=" + rn.Enabled);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[OPC-FAIL] " + ex.GetType().Name + " :: " + FirstLine(ex.Message));
                }
                DeviceItem cpu = FindCpu(device.DeviceItems);
                if (cpu != null)
                {
                    try
                    {
                        foreach (EngineeringAttributeInfo info in ((IEngineeringObject)cpu).GetAttributeInfos())
                        {
                            if (info.Name.IndexOf("Opc", StringComparison.OrdinalIgnoreCase) >= 0 || info.Name.IndexOf("OPC", StringComparison.Ordinal) >= 0)
                            {
                                object value = null;
                                try { value = ((IEngineeringObject)cpu).GetAttribute(info.Name); } catch (Exception) { }
                                Console.WriteLine("[OPC-ATTR] " + info.Name + " = " + (value == null ? "null" : value.ToString()));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[OPC-ATTR-FAIL] " + ex.GetType().Name);
                    }
                }
            }
        }
    }

    private void AddTag(string name, string address)
    {
        foreach (Project project in tia.Projects)
        {
            foreach (Device device in project.Devices)
            {
                PlcSoftware software = GetPlcSoftware(device.DeviceItems);
                if (software == null)
                {
                    continue;
                }
                PlcTagTable table = null;
                foreach (PlcTagTable candidate in software.TagTableGroup.TagTables)
                {
                    if (candidate.Name == "LADGEN_Tags")
                    {
                        table = candidate;
                    }
                }
                if (table == null)
                {
                    Console.WriteLine("[ADDTAG] tag table not found");
                    return;
                }
                foreach (PlcTag tag in table.Tags)
                {
                    if (tag.Name == name)
                    {
                        Console.WriteLine("[ADDTAG] exists: " + name + " = " + tag.LogicalAddress);
                        return;
                    }
                }
                table.Tags.Create(name, "Bool", address);
                Console.WriteLine("[ADDTAG] created: " + name + " = " + address);
            }
        }
    }

    private void Audit()
    {
        OnlineStateReport();
        foreach (Project project in tia.Projects)
        {
            Console.WriteLine("[AUDIT] project: " + project.Name + " | " + project.Path);
            foreach (Device device in project.Devices)
            {
                PlcSoftware software = GetPlcSoftware(device.DeviceItems);
                if (software == null)
                {
                    Console.WriteLine("[AUDIT] no PlcSoftware on " + device.Name);
                    continue;
                }
                Console.WriteLine("[AUDIT] software: " + software.Name);
                foreach (PlcTagTable table in software.TagTableGroup.TagTables)
                {
                    Console.WriteLine("[TABLE] " + table.Name);
                    int count = 0;
                    foreach (PlcTag tag in table.Tags)
                    {
                        count++;
                        Console.WriteLine("[TAG] " + table.Name + " | " + tag.Name + " | " + tag.LogicalAddress + " | " + tag.DataTypeName);
                    }
                    Console.WriteLine("[TABLE] " + table.Name + " count=" + count);
                }
                int blockCount = 0;
                foreach (PlcBlock block in software.BlockGroup.Blocks)
                {
                    blockCount++;
                    Console.WriteLine("[ABLOCK] " + block.Name + " | " + block.Number + " | " + block.ProgrammingLanguage);
                }
                Console.WriteLine("[ABLOCK] total=" + blockCount);
                IEngineeringServiceProvider sp = software as IEngineeringServiceProvider;
                ICompilable compilable = sp == null ? null : sp.GetService<ICompilable>();
                if (compilable != null)
                {
                    Console.WriteLine("[AUDIT] compiling ...");
                    CompilerResult r = compilable.Compile();
                    Console.WriteLine("[AUDIT-COMPILE] state=" + r.State + " errors=" + r.ErrorCount + " warnings=" + r.WarningCount);
                    foreach (CompilerResultMessage m in r.Messages)
                    {
                        PrintMessageTree(m, 0);
                    }
                }
            }
        }
    }

    private void OnlineStateReport()
    {
        foreach (Project project in tia.Projects)
        {
            foreach (Device device in project.Devices)
            {
                foreach (DeviceItem item in device.DeviceItems)
                {
                    IEngineeringServiceProvider provider = item as IEngineeringServiceProvider;
                    if (provider == null)
                    {
                        continue;
                    }
                    try
                    {
                        OnlineProvider online = provider.GetService<OnlineProvider>();
                        if (online != null)
                        {
                            Console.WriteLine("[ONLINE] " + item.Name + " state=" + online.State);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[ONLINE-WARN] " + item.Name + " :: " + ex.GetType().Name);
                    }
                    foreach (DeviceItem child in item.DeviceItems)
                    {
                        IEngineeringServiceProvider cp = child as IEngineeringServiceProvider;
                        if (cp == null)
                        {
                            continue;
                        }
                        try
                        {
                            OnlineProvider co = cp.GetService<OnlineProvider>();
                            if (co != null)
                            {
                                Console.WriteLine("[ONLINE] " + child.Name + " state=" + co.State);
                            }
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
            }
        }
    }

    private void ResetDemo()
    {
        string[] demoBlocks = new string[] { "FC_LADGEN", "FB_GEN_TIMER", "FB_DCOIL_TEST", "FC_GEN_TEST" };
        string[] tags = new string[]
        {
            "V3M_启动按钮|I0.0", "V3M_急停|I0.1", "V3M_复位按钮|I0.2",
            "V3M_上层使能|M30.0", "V3M_主动轮使能|M30.1", "V3M_链结使能|M30.2", "V3M_切割器使能|M30.3",
            "V3M_挡板使能|M30.4", "V3M_挡板位置状态|M30.5", "V3M_丝杆使能|M30.6", "V3M_丝杆位置状态|M30.7",
            "V3M_送料1使能|M31.0", "V3M_送料1位置状态|M31.1", "V3M_送料2使能|M31.2", "V3M_送料2位置状态|M31.3",
            "V3M_吸嘴使能|M31.4", "V3M_吸嘴位置状态|M31.5", "V3M_撑袋器使能|M31.6", "V3M_撑袋器位置状态|M31.7"
        };
        foreach (Project project in tia.Projects)
        {
            foreach (Device device in project.Devices)
            {
                PlcSoftware software = GetPlcSoftware(device.DeviceItems);
                if (software == null)
                {
                    continue;
                }
                try
                {
                    for (int i = 0; i < demoBlocks.Length; i++)
                    {
                        PlcBlock block = software.BlockGroup.Blocks.Find(demoBlocks[i]);
                        if (block != null)
                        {
                            block.Delete();
                            Console.WriteLine("[RESET] deleted block " + demoBlocks[i]);
                        }
                    }
                    PlcTagTable table = null;
                    foreach (PlcTagTable candidate in software.TagTableGroup.TagTables)
                    {
                        if (candidate.Name == "LADGEN_Tags")
                        {
                            table = candidate;
                        }
                    }
                    if (table == null)
                    {
                        table = software.TagTableGroup.TagTables.Create("LADGEN_Tags");
                    }
                    List<string> existing = new List<string>();
                    foreach (PlcTag tag in table.Tags)
                    {
                        existing.Add(tag.Name);
                    }
                    for (int i = 0; i < existing.Count; i++)
                    {
                        PlcTag tag = table.Tags.Find(existing[i]);
                        if (tag != null)
                        {
                            tag.Delete();
                        }
                    }
                    Console.WriteLine("[RESET] cleared tags: " + existing.Count);
                    for (int i = 0; i < tags.Length; i++)
                    {
                        string[] parts = tags[i].Split('|');
                        table.Tags.Create(parts[0], "Bool", parts[1]);
                        Console.WriteLine("[RESET] tag " + parts[0] + " = " + parts[1]);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[RESET-FAIL] " + ex.GetType().FullName); Console.WriteLine(ex.ToString());
                }
            }
        }
    }

    private void GoOfflineAll()
    {
        foreach (Project project in tia.Projects)
        {
            foreach (Device device in project.Devices)
            {
                foreach (DeviceItem item in device.DeviceItems)
                {
                    TryGoOffline(item, 0);
                }
            }
        }
    }

    private static void TryGoOffline(DeviceItem item, int depth)
    {
        string pad = new string(' ', depth * 2);
        IEngineeringServiceProvider provider = item as IEngineeringServiceProvider;
        if (provider != null)
        {
            try
            {
                OnlineProvider online = provider.GetService<OnlineProvider>();
                if (online != null)
                {
                    online.GoOffline();
                    Console.WriteLine("[OFFLINE] " + pad + item.Name + " -> GoOffline called");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[OFFLINE-WARN] " + pad + item.Name + " :: " + ex.GetType().Name + " :: " + FirstLine(ex.Message));
            }
        }
        foreach (DeviceItem child in item.DeviceItems)
        {
            TryGoOffline(child, depth + 1);
        }
    }

    private void MakeInstanceDb()
    {
        foreach (Project project in tia.Projects)
        {
            foreach (Device device in project.Devices)
            {
                PlcSoftware software = GetPlcSoftware(device.DeviceItems);
                if (software == null)
                {
                    continue;
                }
                try
                {
                    PlcBlock existing = software.BlockGroup.Blocks.Find("FB_GEN_MAIN_DB");
                    if (existing != null)
                    {
                        existing.Delete();
                        Console.WriteLine("[IDB] deleted old instance DB");
                    }
                    InstanceDB db = software.BlockGroup.Blocks.CreateInstanceDB("FB_GEN_MAIN_DB", false, 9101, "FB_GEN_MAIN");
                    Console.WriteLine("[IDB] created: " + db.Name + " number=" + db.Number);
                    IEngineeringServiceProvider sp = software as IEngineeringServiceProvider;
                    ICompilable compilable = sp == null ? null : sp.GetService<ICompilable>();
                    if (compilable != null)
                    {
                        CompilerResult r = compilable.Compile();
                        Console.WriteLine("[IDB-COMPILE] state=" + r.State + " errors=" + r.ErrorCount + " warnings=" + r.WarningCount);
                        foreach (CompilerResultMessage m in r.Messages)
                        {
                            PrintMessageTree(m, 0);
                        }
                    }
                    PlcBlock dbBlock = software.BlockGroup.Blocks.Find("FB_GEN_MAIN_DB");
                    if (dbBlock != null)
                    {
                        string p = Path.Combine(outDir, "exported_IDB.xml");
                        dbBlock.Export(new FileInfo(p), ExportOptions.None);
                        Console.WriteLine("[IDB] exported: " + p);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[IDB-FAIL] " + ex.GetType().FullName);
                    Console.WriteLine(ex.ToString());
                }
            }
        }
    }

    private void CreateMainTags()
    {
        string[] names = new string[]
        {
            "启动按钮|Bool|M10.0", "复位按钮|Bool|M10.1", "连续模式|Bool|M10.2", "放料指示灯|Bool|M10.3",
            "主动轮使能|Bool|M12.0", "切割器使能|Bool|M12.1", "链结使能|Bool|M12.2",
            "吸嘴使能|Bool|M20.0", "吸嘴位置状态|Bool|M20.1", "撑袋器使能|Bool|M20.2", "撑袋器位置状态|Bool|M20.3",
            "丝杆使能|Bool|M20.4", "丝杆位置状态|Bool|M20.5", "挡板使能|Bool|M20.6", "挡板位置状态|Bool|M20.7",
            "送料1使能|Bool|M21.0", "送料1位置状态|Bool|M21.1", "送料2使能|Bool|M21.2", "送料2位置状态|Bool|M21.3",
            "T_3S|Time|MD10", "T_6S|Time|MD14", "T_8S|Time|MD18"
        };
        foreach (Project project in tia.Projects)
        {
            foreach (Device device in project.Devices)
            {
                PlcSoftware software = GetPlcSoftware(device.DeviceItems);
                if (software == null)
                {
                    continue;
                }
                try
                {
                    PlcTagTable table = null;
                    foreach (PlcTagTable candidate in software.TagTableGroup.TagTables)
                    {
                        if (candidate.Name == "LADGEN_Tags")
                        {
                            table = candidate;
                        }
                    }
                    if (table == null)
                    {
                        table = software.TagTableGroup.TagTables.Create("LADGEN_Tags");
                        Console.WriteLine("[TAGS] table created");
                    }
                    List<string> existing = new List<string>();
                    foreach (PlcTag tag in table.Tags)
                    {
                        existing.Add(tag.Name);
                    }
                    for (int i = 0; i < names.Length; i++)
                    {
                        string[] parts = names[i].Split('|');
                        if (existing.Contains(parts[0]))
                        {
                            continue;
                        }
                        try
                        {
                            table.Tags.Create(parts[0], parts[1], parts[2]);
                            Console.WriteLine("[TAGS] " + parts[0] + " = " + parts[2] + " (" + parts[1] + ")");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[TAGS-FAIL] " + parts[0] + " :: " + FirstLine(ex.Message));
                        }
                    }
                    Console.WriteLine("[TAGS] done");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[TAGS-FAIL] " + ex.GetType().Name + " :: " + FirstLine(ex.Message));
                }
            }
        }
    }

    private void TimerTest()
    {
        string xmlPath = Path.Combine(outDir, "gen_timer.xml");
        if (!File.Exists(xmlPath))
        {
            Console.WriteLine("[TIMER] missing " + xmlPath);
            return;
        }
        foreach (Project project in tia.Projects)
        {
            foreach (Device device in project.Devices)
            {
                PlcSoftware software = GetPlcSoftware(device.DeviceItems);
                if (software == null)
                {
                    continue;
                }
                try
                {
                    PlcTagTable table = null;
                    foreach (PlcTagTable candidate in software.TagTableGroup.TagTables)
                    {
                        if (candidate.Name == "LADGEN_Tags")
                        {
                            table = candidate;
                        }
                    }
                    if (table == null)
                    {
                        table = software.TagTableGroup.TagTables.Create("LADGEN_Tags");
                    }
                    bool hasTag = false;
                    foreach (PlcTag tag in table.Tags)
                    {
                        if (tag.Name == "T_3S")
                        {
                            hasTag = true;
                        }
                    }
                    if (!hasTag)
                    {
                        table.Tags.Create("T_3S", "Time", "MD10");
                        Console.WriteLine("[TIMER] tag T_3S created at MD10");
                    }
                    IList<PlcBlock> imported = software.BlockGroup.Blocks.Import(new FileInfo(xmlPath), ImportOptions.Override);
                    Console.WriteLine("[TIMER-IMPORT-OK] count=" + imported.Count);
                    foreach (PlcBlock block in imported)
                    {
                        Console.WriteLine("[TIMER-BLOCK] " + block.Name + " lang=" + block.ProgrammingLanguage);
                    }
                    IEngineeringServiceProvider sp = software as IEngineeringServiceProvider;
                    ICompilable compilable = sp == null ? null : sp.GetService<ICompilable>();
                    if (compilable != null)
                    {
                        CompilerResult r = compilable.Compile();
                        Console.WriteLine("[TIMER-COMPILE] state=" + r.State + " errors=" + r.ErrorCount + " warnings=" + r.WarningCount);
                        int n = 0;
                        foreach (CompilerResultMessage m in r.Messages)
                        {
                            if (n++ >= 12) { break; }
                            Console.WriteLine("[TIMER-MSG] " + m.State + " | path=" + m.Path + " | " + FirstLine(m.Description));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[TIMER-FAIL] " + ex.GetType().FullName);
                    Console.WriteLine(ex.ToString());
                }
            }
        }
    }

    private void Services()
    {
        foreach (Project project in tia.Projects)
        {
            foreach (Device device in project.Devices)
            {
                PlcSoftware software = GetPlcSoftware(device.DeviceItems);
                if (software == null)
                {
                    continue;
                }
                Console.WriteLine("[OBJ] PlcSoftware isCompilable=" + (software is ICompilable));
                PrintServices(software as IEngineeringServiceProvider, "PlcSoftware");
                Console.WriteLine("[OBJ] BlockGroup isCompilable=" + (software.BlockGroup is ICompilable));
                PrintServices(software.BlockGroup as IEngineeringServiceProvider, "BlockGroup");
                DeviceItem cpu = FindCpu(device.DeviceItems);
                if (cpu != null)
                {
                    Console.WriteLine("[OBJ] CPU isCompilable=" + (cpu is ICompilable));
                    PrintServices(cpu as IEngineeringServiceProvider, "CPU");
                }
                Console.WriteLine("[OBJ] Device isCompilable=" + (device is ICompilable));
                PrintServices(device as IEngineeringServiceProvider, "Device");
            }
        }
    }

    private static void PrintServices(IEngineeringServiceProvider provider, string label)
    {
        if (provider == null)
        {
            Console.WriteLine("[SVC] " + label + " : provider null");
            return;
        }
        try
        {
            foreach (EngineeringServiceInfo info in provider.GetServiceInfos())
            {
                Console.WriteLine("[SVC] " + label + " :: " + info.Type);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[SVC-FAIL] " + label + " :: " + ex.GetType().Name + " :: " + FirstLine(ex.Message));
        }
    }

    private void GenFromCsv()
    {
        string csvPath = Path.Combine(outDir, "points.csv");
        if (!File.Exists(csvPath))
        {
            Console.WriteLine("[GEN] missing " + csvPath);
            return;
        }
        List<string[]> rows = new List<string[]>();
        string[] lines = File.ReadAllLines(csvPath);
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("#"))
            {
                continue;
            }
            rows.Add(line.Split(','));
        }
        Console.WriteLine("[GEN] rows=" + rows.Count);
        if (rows.Count == 0)
        {
            return;
        }
        foreach (Project project in tia.Projects)
        {
            foreach (Device device in project.Devices)
            {
                PlcSoftware software = GetPlcSoftware(device.DeviceItems);
                if (software == null)
                {
                    continue;
                }
                try
                {
                    PlcTagTable table = null;
                    foreach (PlcTagTable candidate in software.TagTableGroup.TagTables)
                    {
                        if (candidate.Name == "LADGEN_Tags")
                        {
                            table = candidate;
                        }
                    }
                    if (table == null)
                    {
                        table = software.TagTableGroup.TagTables.Create("LADGEN_Tags");
                        Console.WriteLine("[GEN] tag table created: " + table.Name);
                    }
                    List<string> existing = new List<string>();
                    foreach (PlcTag tag in table.Tags)
                    {
                        existing.Add(tag.Name);
                    }
                    for (int r = 0; r < rows.Count; r++)
                    {
                        string[] cells = rows[r];
                        string inTag = cells[2].Trim();
                        string inAddr = cells[3].Trim();
                        string outTag = cells[4].Trim();
                        string outAddr = cells[5].Trim();
                        if (!existing.Contains(inTag))
                        {
                            table.Tags.Create(inTag, "Bool", inAddr);
                            existing.Add(inTag);
                            Console.WriteLine("[GEN] tag " + inTag + " = " + inAddr);
                        }
                        if (!existing.Contains(outTag))
                        {
                            table.Tags.Create(outTag, "Bool", outAddr);
                            existing.Add(outTag);
                            Console.WriteLine("[GEN] tag " + outTag + " = " + outAddr);
                        }
                    }
                    string xml = BuildLadXml(rows);
                    string xmlPath = Path.Combine(outDir, "gen_ladgen.xml");
                    File.WriteAllText(xmlPath, xml, new UTF8Encoding(false));
                    Console.WriteLine("[GEN] xml written: " + xmlPath + " (" + xml.Length + " chars)");
                    IList<PlcBlock> imported = software.BlockGroup.Blocks.Import(new FileInfo(xmlPath), ImportOptions.Override);
                    Console.WriteLine("[GEN-IMPORT-OK] count=" + imported.Count);
                    foreach (PlcBlock block in imported)
                    {
                        Console.WriteLine("[GEN-BLOCK] " + block.Name + " lang=" + block.ProgrammingLanguage + " number=" + block.Number);
                    }
                    IEngineeringServiceProvider serviceProvider = software as IEngineeringServiceProvider;
                    ICompilable compilable = serviceProvider == null ? null : serviceProvider.GetService<ICompilable>();
                    if (compilable == null)
                    {
                        Console.WriteLine("[GEN-COMPILE] ICompilable service not available");
                    }
                    else
                    {
                        Console.WriteLine("[GEN-COMPILE] compiling...");
                        CompilerResult result = compilable.Compile();
                        Console.WriteLine("[GEN-COMPILE] state=" + result.State + " errors=" + result.ErrorCount + " warnings=" + result.WarningCount);
                        int shown = 0;
                        foreach (CompilerResultMessage message in result.Messages)
                        {
                            if (shown++ >= 15)
                            {
                                break;
                            }
                            Console.WriteLine("[GEN-MSG] " + message.State + " | " + FirstLine(message.Description));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[GEN-FAIL] " + ex.GetType().FullName);
                    Console.WriteLine(ex.ToString());
                }
            }
        }
    }

    private static string BuildLadXml(List<string[]> rows)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<Document>");
        sb.AppendLine("  <Engineering version=\"V21\" />");
        sb.AppendLine("  <SW.Blocks.FC ID=\"0\">");
        sb.AppendLine("    <AttributeList>");
        sb.AppendLine("      <Interface>");
        sb.AppendLine("        <Sections xmlns=\"http://www.siemens.com/automation/Openness/SW/Interface/v5\">");
        sb.AppendLine("          <Section Name=\"Input\" /><Section Name=\"Output\" /><Section Name=\"InOut\" />");
        sb.AppendLine("          <Section Name=\"Return\" /><Section Name=\"Temp\" /><Section Name=\"Constant\" />");
        sb.AppendLine("        </Sections>");
        sb.AppendLine("      </Interface>");
        sb.AppendLine("      <MemoryLayout>Optimized</MemoryLayout>");
        sb.AppendLine("      <Name>FC_LADGEN</Name>");
        sb.AppendLine("      <Namespace></Namespace>");
        sb.AppendLine("      <Number>9100</Number>");
        sb.AppendLine("      <ProgrammingLanguage>LAD</ProgrammingLanguage>");
        sb.AppendLine("    </AttributeList>");
        sb.AppendLine("    <ObjectList>");
        for (int r = 0; r < rows.Count; r++)
        {
            string[] cells = rows[r];
            string station = cells[0].Trim();
            string inTag = cells[2].Trim();
            string outTag = cells[4].Trim();
            int cuId = r + 1;
            int titleId = 100 + r * 4;
            int titleItemId = 101 + r * 4;
            int commentId = 102 + r * 4;
            int commentItemId = 103 + r * 4;
            sb.AppendLine("      <SW.Blocks.CompileUnit ID=\"" + cuId + "\" CompositionName=\"CompileUnits\">");
            sb.AppendLine("        <AttributeList>");
            sb.AppendLine("          <NetworkSource>");
            sb.AppendLine("            <FlgNet xmlns=\"http://www.siemens.com/automation/Openness/SW/NetworkSource/FlgNet/v5\">");
            sb.AppendLine("              <Parts>");
            sb.AppendLine("                <Access Scope=\"GlobalVariable\" UId=\"21\"><Symbol><Component Name=\"" + inTag + "\" /></Symbol></Access>");
            sb.AppendLine("                <Part Name=\"Contact\" UId=\"22\" />");
            sb.AppendLine("                <Part Name=\"Coil\" UId=\"23\" />");
            sb.AppendLine("                <Access Scope=\"GlobalVariable\" UId=\"24\"><Symbol><Component Name=\"" + outTag + "\" /></Symbol></Access>");
            sb.AppendLine("              </Parts>");
            sb.AppendLine("              <Wires>");
            sb.AppendLine("                <Wire UId=\"31\"><Powerrail /><NameCon UId=\"22\" Name=\"in\" /></Wire>");
            sb.AppendLine("                <Wire UId=\"32\"><NameCon UId=\"22\" Name=\"out\" /><NameCon UId=\"23\" Name=\"in\" /></Wire>");
            sb.AppendLine("                <Wire UId=\"33\"><NameCon UId=\"22\" Name=\"operand\" /><IdentCon UId=\"21\" /></Wire>");
            sb.AppendLine("                <Wire UId=\"34\"><NameCon UId=\"23\" Name=\"operand\" /><IdentCon UId=\"24\" /></Wire>");
            sb.AppendLine("                <Wire UId=\"35\"><NameCon UId=\"23\" Name=\"out\" /><OpenCon UId=\"36\" /></Wire>");
            sb.AppendLine("              </Wires>");
            sb.AppendLine("            </FlgNet>");
            sb.AppendLine("          </NetworkSource>");
            sb.AppendLine("          <ProgrammingLanguage>LAD</ProgrammingLanguage>");
            sb.AppendLine("        </AttributeList>");
            sb.AppendLine("        <ObjectList>");
            sb.AppendLine("          <MultilingualText ID=\"" + titleId + "\" CompositionName=\"Title\">");
            sb.AppendLine("            <ObjectList><MultilingualTextItem ID=\"" + titleItemId + "\" CompositionName=\"Items\">");
            sb.AppendLine("              <AttributeList><Culture>zh-CN</Culture><Text>" + station + "</Text></AttributeList>");
            sb.AppendLine("            </MultilingualTextItem></ObjectList>");
            sb.AppendLine("          </MultilingualText>");
            sb.AppendLine("          <MultilingualText ID=\"" + commentId + "\" CompositionName=\"Comment\">");
            sb.AppendLine("            <ObjectList><MultilingualTextItem ID=\"" + commentItemId + "\" CompositionName=\"Items\">");
            sb.AppendLine("              <AttributeList><Culture>zh-CN</Culture><Text></Text></AttributeList>");
            sb.AppendLine("            </MultilingualTextItem></ObjectList>");
            sb.AppendLine("          </MultilingualText>");
            sb.AppendLine("        </ObjectList>");
            sb.AppendLine("      </SW.Blocks.CompileUnit>");
        }
        sb.AppendLine("    </ObjectList>");
        sb.AppendLine("  </SW.Blocks.FC>");
        sb.AppendLine("</Document>");
        return sb.ToString();
    }

    private void CleanupTestBlocks()
    {
        foreach (Project project in tia.Projects)
        {
            foreach (Device device in project.Devices)
            {
                PlcSoftware software = GetPlcSoftware(device.DeviceItems);
                if (software == null)
                {
                    continue;
                }
                foreach (PlcBlock block in software.BlockGroup.Blocks)
                {
                    if (block.Name.StartsWith("FC_GEN_TEST") || block.Name.StartsWith("ZZ_"))
                    {
                        block.Delete();
                        Console.WriteLine("[CLEANED] " + block.Name);
                    }
                }
            }
        }
    }

    private void ImportTest()
    {
        string[] files = Directory.GetFiles(outDir, "gen*.xml");
        Array.Sort(files);
        foreach (string file in files)
        {
            Console.WriteLine("[TRY] " + Path.GetFileName(file));
            string path = file;
        foreach (Project project in tia.Projects)
        {
            foreach (Device device in project.Devices)
            {
                PlcSoftware software = GetPlcSoftware(device.DeviceItems);
                if (software == null)
                {
                    continue;
                }
                try
                {
                    IList<PlcBlock> imported = software.BlockGroup.Blocks.Import(new FileInfo(path), ImportOptions.Override);
                    Console.WriteLine("[IMPORT-OK] count=" + imported.Count);
                    foreach (PlcBlock block in imported)
                    {
                        Console.WriteLine("[IMPORTED] " + block.Name + " lang=" + block.ProgrammingLanguage);
                    }

                }
                catch (Exception ex)
                {
                    Console.WriteLine("[IMPORT-FAIL] " + Path.GetFileName(path) + " :: " + ex.GetType().FullName);
                    Console.WriteLine(ex.ToString());
                    continue;
                }
            }
        }
        }
        foreach (Project project in tia.Projects)
        {
            foreach (Device device in project.Devices)
            {
                PlcSoftware software = GetPlcSoftware(device.DeviceItems);
                if (software == null)
                {
                    continue;
                }
                try
                {
                    IEngineeringServiceProvider sp2 = software as IEngineeringServiceProvider;
                    ICompilable compilable = sp2 == null ? null : sp2.GetService<ICompilable>();
                    if (compilable != null)
                    {
                        CompilerResult r = compilable.Compile();
                        Console.WriteLine("[IMPORT-COMPILE] state=" + r.State + " errors=" + r.ErrorCount + " warnings=" + r.WarningCount);
                        int n = 0;
                        foreach (CompilerResultMessage m in r.Messages)
                        {
                            if (n++ >= 12) { break; }
                            Console.WriteLine("[IMPORT-MSG] " + m.State + " | " + FirstLine(m.Description));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[IMPORT-COMPILE-FAIL] " + ex.GetType().Name + " :: " + FirstLine(ex.Message));
                }
            }
        }
    }

    private void Diagnostics()
    {
        foreach (Project project in tia.Projects)
        {
            foreach (Device device in project.Devices)
            {
                PlcSoftware software = GetPlcSoftware(device.DeviceItems);
                if (software == null)
                {
                    continue;
                }
                Console.WriteLine("[SW] " + software.Name + " type=" + software.GetType().FullName);
                Type[] interfaces = software.GetType().GetInterfaces();
                for (int i = 0; i < interfaces.Length; i++)
                {
                    Console.WriteLine("[IFACE] " + interfaces[i].FullName);
                }
                PrintInvocations((IEngineeringObject)software, "PlcSoftware");
                DeviceItem cpu = FindCpu(device.DeviceItems);
                if (cpu != null)
                {
                    PrintInvocations((IEngineeringObject)cpu, "CPU(" + cpu.Name + ")");
                    IEngineeringServiceProvider provider = cpu as IEngineeringServiceProvider;
                    if (provider != null)
                    {
                        foreach (EngineeringServiceInfo info in provider.GetServiceInfos())
                        {
                            Console.WriteLine("[SERVICE] " + info.Type + " | " + info.ToString());
                        }
                    }
                }
            }
        }
    }

    private static void PrintInvocations(IEngineeringObject target, string label)
    {
        try
        {
            foreach (EngineeringInvocationInfo info in target.GetInvocationInfos())
            {
                Console.WriteLine("[INVOKE] " + label + " :: " + info.Name);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[INVOKE-FAIL] " + label + " :: " + ex.GetType().Name);
        }
    }

    private void SkeletonFromOpenProject()
    {
        foreach (Project project in tia.Projects)
        {
            foreach (Device device in project.Devices)
            {
                PlcSoftware software = GetPlcSoftware(device.DeviceItems);
                if (software == null)
                {
                    continue;
                }
                Console.WriteLine("[SW] " + project.Name + " / " + software.Name);
                try
                {
                    CompilerResult result = ReflectionCompile(software);
                    if (result == null)
                    {
                        Console.WriteLine("[WARN] compile not performed");
                    }
                    else
                    {
                        Console.WriteLine("[COMPILE] state=" + result.State + " errors=" + result.ErrorCount + " warnings=" + result.WarningCount);
                        int shown = 0;
                        foreach (CompilerResultMessage message in result.Messages)
                        {
                            if (shown++ >= 10)
                            {
                                break;
                            }
                            Console.WriteLine("[COMPILE-MSG] " + message.State + " | " + FirstLine(message.Description));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[COMPILE-FAIL] " + ex.GetType().FullName + " :: " + FirstLine(ex.Message));
                }
                DirectoryInfo dir = new DirectoryInfo(Path.Combine(outDir, "blocks"));
                if (!dir.Exists)
                {
                    dir.Create();
                }
                DumpGroup(software.BlockGroup, dir, software.Name);
            }
        }
    }

    private static CompilerResult ReflectionCompile(PlcSoftware software)
    {
        Assembly assembly = typeof(TiaPortal).Assembly;
        Type providerType = assembly.GetType("Siemens.Engineering.Compiler.CompileProvider");
        if (providerType == null)
        {
            Console.WriteLine("[COMPILE] CompileProvider type not found");
            return null;
        }
        IEngineeringServiceProvider serviceProvider = software as IEngineeringServiceProvider;
        if (serviceProvider == null)
        {
            Console.WriteLine("[COMPILE] PlcSoftware is not IEngineeringServiceProvider");
            return null;
        }
        object provider = null;
        MethodInfo[] methods = typeof(IEngineeringServiceProvider).GetMethods();
        for (int i = 0; i < methods.Length; i++)
        {
            if (methods[i].Name == "GetService" && methods[i].IsGenericMethodDefinition)
            {
                provider = methods[i].MakeGenericMethod(providerType).Invoke(serviceProvider, null);
                break;
            }
        }
        if (provider == null)
        {
            Console.WriteLine("[COMPILE] provider is null");
            return null;
        }
        MethodInfo compile = providerType.GetMethod("Compile");
        Console.WriteLine("[COMPILE] invoking Compile() via reflection");
        return (CompilerResult)compile.Invoke(provider, null);
    }

    private void ExportAllBlocks()
    {
        DirectoryInfo dir = new DirectoryInfo(Path.Combine(outDir, "blocks"));
        if (!dir.Exists)
        {
            dir.Create();
        }
        foreach (Project project in tia.Projects)
        {
            foreach (Device device in project.Devices)
            {
                PlcSoftware software = GetPlcSoftware(device.DeviceItems);
                if (software == null)
                {
                    continue;
                }
                Console.WriteLine("[SW] " + project.Name + " / " + software.Name);
                DumpGroup(software.BlockGroup, dir, software.Name);
            }
        }
    }

    private void DumpGroup(PlcBlockGroup group, DirectoryInfo dir, string prefix)
    {
        foreach (PlcBlock block in group.Blocks)
        {
            Console.WriteLine("[BLOCK] " + prefix + "/" + block.Name + " lang=" + block.ProgrammingLanguage);
            try
            {
                string target = Path.Combine(dir.FullName, Safe(prefix + "_" + block.Name) + ".xml");
                block.Export(new FileInfo(target), ExportOptions.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[BLOCK-FAIL] " + block.Name);
                Console.WriteLine(ex.ToString());
                try
                {
                    Console.WriteLine("[RETRY] ExclusiveAccess + WithDefaults");
                    ExclusiveAccess ea = tia.ExclusiveAccess("Export " + block.Name);
                    try
                    {
                        string target2 = Path.Combine(dir.FullName, Safe(prefix + "_" + block.Name) + "_v2.xml");
                        block.Export(new FileInfo(target2), ExportOptions.WithDefaults);
                        Console.WriteLine("[RETRY-OK] " + target2);
                    }
                    finally
                    {
                        ea.Dispose();
                    }
                }
                catch (Exception ex2)
                {
                    Console.WriteLine("[RETRY-FAIL] " + ex2.ToString());
                }
            }
        }
        foreach (PlcBlockGroup sub in group.Groups)
        {
            DumpGroup(sub, dir, prefix + "/" + sub.Name);
        }
    }

    private static string Safe(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalid.Length; i++)
        {
            name = name.Replace(invalid[i], '_');
        }
        return name;
    }

    private static PlcSoftware GetPlcSoftware(DeviceItemComposition items)
    {
        if (items == null)
        {
            return null;
        }
        foreach (DeviceItem item in items)
        {
            PlcSoftware direct = null;
            try
            {
                SoftwareContainer container = item.GetService<SoftwareContainer>();
                if (container != null && container.Software is PlcSoftware)
                {
                    direct = (PlcSoftware)container.Software;
                }
            }
            catch (Exception)
            {
            }
            if (direct != null)
            {
                return direct;
            }
            PlcSoftware nested = GetPlcSoftware(item.DeviceItems);
            if (nested != null)
            {
                return nested;
            }
        }
        return null;
    }

    private static DeviceItem FindCpu(DeviceItemComposition items)
    {
        if (items == null)
        {
            return null;
        }
        foreach (DeviceItem item in items)
        {
            string typeId = "";
            try
            {
                typeId = item.TypeIdentifier;
            }
            catch (Exception)
            {
            }
            if (typeId != null && typeId.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return item;
            }
            DeviceItem nested = FindCpu(item.DeviceItems);
            if (nested != null)
            {
                return nested;
            }
        }
        return null;
    }

    private static string FirstLine(string text)
    {
        if (text == null)
        {
            return "";
        }
        int idx = text.IndexOf('\n');
        return idx < 0 ? text : text.Substring(0, idx);
    }
}





































