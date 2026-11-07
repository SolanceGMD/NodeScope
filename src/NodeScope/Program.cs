using System.Diagnostics;
using System.Runtime.InteropServices;
namespace NodeScope;
internal static class Program { [STAThread] private static void Main() { ApplicationConfiguration.Initialize(); Application.Run(new LauncherForm()); } }
internal sealed class LauncherForm : Form {
 private readonly ListView games = new() { Dock=DockStyle.Fill, View=View.Details, FullRowSelect=true, MultiSelect=false };
 private readonly Label status = new() { Dock=DockStyle.Bottom, Height=42, TextAlign=ContentAlignment.MiddleLeft, Padding=new Padding(10), Text="Select a running game window." };
 private readonly string dllPath=Path.Combine(AppContext.BaseDirectory,"NodeScopeHook.dll");
 public LauncherForm() {
  Text="NodeScope · Runtime Injector"; Width=760; Height=510; MinimumSize=new Size(620,380); StartPosition=FormStartPosition.CenterScreen;
  games.Columns.Add("Game window",430); games.Columns.Add("Process",180); games.Columns.Add("PID",90);
  var top=new FlowLayoutPanel { Dock=DockStyle.Top, Height=54, Padding=new Padding(10), WrapContents=false };
  var refresh=new Button { Text="Refresh", Width=100, Height=30 }; var inject=new Button { Text="Inject NodeScope", Width=150, Height=30 };
  refresh.Click+=(_,_)=>RefreshGames(); inject.Click+=(_,_)=>InjectSelected(); top.Controls.Add(refresh); top.Controls.Add(inject);
  var note=new Label { Dock=DockStyle.Bottom, Height=58, Padding=new Padding(10,4,10,4), Text="Experimental native runtime hook. This build demonstrates in-process loading and an in-game status overlay; Godot scene-tree inspection/editing is not implemented yet." };
  Controls.Add(games); Controls.Add(top); Controls.Add(note); Controls.Add(status); RefreshGames();
 }
 private void RefreshGames() {
  games.Items.Clear();
  foreach(var p in Process.GetProcesses().OrderBy(p=>p.ProcessName)) {
   try { if(p.Id==Environment.ProcessId||p.MainWindowHandle==IntPtr.Zero) continue; var title=p.MainWindowTitle; if(string.IsNullOrWhiteSpace(title)) continue;
    var item=new ListViewItem(title){Tag=p.Id}; item.SubItems.Add(p.ProcessName); item.SubItems.Add(p.Id.ToString()); games.Items.Add(item);
   } catch {} finally { p.Dispose(); }
  }
  status.Text=$"Found {games.Items.Count} visible application windows. Select the game process.";
 }
 private void InjectSelected() {
  if(games.SelectedItems.Count==0){status.Text="Select a running game first.";return;}
  if(!File.Exists(dllPath)){status.Text="NodeScopeHook.dll is missing. Keep it beside NodeScope.exe.";return;}
  var pid=(int)games.SelectedItems[0].Tag!;
  try { var ok=NativeInjector.Inject(pid,dllPath); status.Text=ok?$"Injection request completed for PID {pid}. Press = in the game to toggle the overlay.":"Injection failed. Check process permissions and architecture."; }
  catch(Exception ex){status.Text="Injection failed: "+ex.Message;}
 }
}
internal static class NativeInjector {
 const uint CREATE_THREAD=0x0002,QUERY=0x0400,VM_OPERATION=0x0008,VM_WRITE=0x0020,COMMIT=0x1000,RESERVE=0x2000,PAGE_RW=0x04;
 [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
 [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr VirtualAllocEx(IntPtr process,IntPtr address,UIntPtr size,uint type,uint protect);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool WriteProcessMemory(IntPtr process,IntPtr address,byte[] buffer,UIntPtr size,out UIntPtr written);
 [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
 [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module,string name);
 [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr CreateRemoteThread(IntPtr process,IntPtr attrs,UIntPtr stack,IntPtr start,IntPtr param,uint flags,out uint id);
 [DllImport("kernel32.dll",SetLastError=true)] static extern uint WaitForSingleObject(IntPtr handle,uint ms);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetExitCodeThread(IntPtr thread,out uint code);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool VirtualFreeEx(IntPtr process,IntPtr address,UIntPtr size,uint type);
 public static bool Inject(int pid,string path) {
  var bytes=System.Text.Encoding.Unicode.GetBytes(Path.GetFullPath(path)+"\0");
  var process=OpenProcess(CREATE_THREAD|QUERY|VM_OPERATION|VM_WRITE,false,pid);
  if(process==IntPtr.Zero) throw new InvalidOperationException($"OpenProcess failed ({Marshal.GetLastWin32Error()}).");
  IntPtr remote=IntPtr.Zero,thread=IntPtr.Zero;
  try {
   remote=VirtualAllocEx(process,IntPtr.Zero,(UIntPtr)bytes.Length,COMMIT|RESERVE,PAGE_RW);
   if(remote==IntPtr.Zero) throw new InvalidOperationException($"VirtualAllocEx failed ({Marshal.GetLastWin32Error()}).");
   if(!WriteProcessMemory(process,remote,bytes,(UIntPtr)bytes.Length,out var written)||written.ToUInt64()!=(ulong)bytes.Length) throw new InvalidOperationException($"WriteProcessMemory failed ({Marshal.GetLastWin32Error()}).");
   var load=GetProcAddress(GetModuleHandle("kernel32.dll"),"LoadLibraryW");
   if(load==IntPtr.Zero) throw new InvalidOperationException("Could not locate LoadLibraryW.");
   thread=CreateRemoteThread(process,IntPtr.Zero,UIntPtr.Zero,load,remote,0,out _);
   if(thread==IntPtr.Zero) throw new InvalidOperationException($"CreateRemoteThread failed ({Marshal.GetLastWin32Error()}).");
   if(WaitForSingleObject(thread,15000)!=0) throw new TimeoutException("Timed out waiting for the target process.");
   return GetExitCodeThread(thread,out var code)&&code!=0;
  } finally { if(thread!=IntPtr.Zero)CloseHandle(thread); if(remote!=IntPtr.Zero)VirtualFreeEx(process,remote,UIntPtr.Zero,0x8000); CloseHandle(process); }
 }
}