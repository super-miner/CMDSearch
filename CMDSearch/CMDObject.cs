using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CMDSearch;

public class CMDObject {
    private string _batchFilePath;
    private bool _logProcessOutput;
    
    private Process _process;
    private string _processOutput = "";
    private StreamReader _processReader;
    private StreamWriter _processWriter;
    private Task _readerTask;

    private Mutex _readerMutex = new Mutex();
    private List<CMDCallback> _callbacks = new List<CMDCallback>();

    private Mutex _threadStateMutex = new Mutex();
    private bool closeThread = false;

    public CMDObject(string batchFilePath, string[] args, bool logProcessOutput = false, bool showWindow = false) {
        _logProcessOutput = logProcessOutput;
        _batchFilePath = batchFilePath;

        string command = $"\"{batchFilePath}\" {string.Join(' ', args)}";
        ProcessStartInfo processInfo = new ProcessStartInfo("cmd.exe", $"/c {command}");
        processInfo.CreateNoWindow = !showWindow;
        processInfo.UseShellExecute = false;
        processInfo.RedirectStandardInput = true;
        processInfo.RedirectStandardOutput = true;

        _process = new Process();
        _process.StartInfo = processInfo;
        
        _process.OutputDataReceived += OnOutputChanged;
        _process.Exited += OnProcessExited;
        
        _process.Start();
        
        _process.BeginOutputReadLine();
        _processWriter = _process.StandardInput;
    }

    public void OnOutputChanged(object? sender, DataReceivedEventArgs args) {
        string data = args.Data;
        
        if (!string.IsNullOrEmpty(data)) {
            _processOutput += $"{data}\n";
            
            if (_logProcessOutput) {
                Debug.WriteLine("\n-------- PROCESS START --------");
                Debug.WriteLine(_processOutput);
                Debug.WriteLine("------- PROCESS CURRENT -------\n");
            }
            
            foreach (CMDCallback callback in _callbacks) {
                MatchCollection matches = callback.Trigger.Matches(_processOutput);
                
                if (matches.Count <= callback.MatchesFound) {
                    continue;
                }
                int matchesLengthDelta = matches.Count - callback.MatchesFound;
                IEnumerable<Match> matchesDelta = matches.Skip(Math.Max(0, matches.Count - matchesLengthDelta));
                    
                foreach (Match match in matchesDelta) {
                    if (match.Success) {
                        Debug.WriteLine("Attempting to call callback");
                        
                        callback.Callback.Invoke(match);
                    }
                }
                    
                callback.MatchesFound = matches.Count;
            }
        } 
    }

    public void AddCallback(string trigger, Action<Match> callback) {
        AddCallback(new Regex(trigger), callback);
    }

    public void AddCallback(Regex trigger, Action<Match> callback) {
        _readerMutex.WaitOne();
        
        _callbacks.Add(new CMDCallback(0, trigger, callback));
        
        _readerMutex.ReleaseMutex();
    }

    public void WriteInput(string input) {
        _processWriter.WriteLine(input);
    }

    public void Destroy() {
        _threadStateMutex.WaitOne();
        
        closeThread = true;
        
        _threadStateMutex.ReleaseMutex();
        
        _processWriter.Close();
        _process.Close();
    }
    
    private void OnProcessExited(object? sender, EventArgs args) {
        Debug.WriteLine("Process Exited");
        
        Destroy();
    }
}