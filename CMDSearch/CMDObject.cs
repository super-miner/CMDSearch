using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CMDSearch;

public class CMDObject {
    private string _batchFilePath;
    private bool _logProcessOutput;
    
    private Process _process;
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

        _process = Process.Start(processInfo) ?? throw new NullReferenceException();
        
        _process.Exited += OnProcessExited;
        _process.Disposed += OnProcessExited;

        _processReader = _process.StandardOutput;
        _processWriter = _process.StandardInput;

        WatchStream();
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
    
    private void WatchStream() {
        SynchronizationContext mainThreadContext = SynchronizationContext.Current;
        
        _readerTask = Task.Run(() => {
            string accumulator = "";
            
            while (true) {
                _threadStateMutex.WaitOne();
                if (closeThread) {
                    closeThread = false;
                    
                    break;
                }
                _threadStateMutex.ReleaseMutex();
                
                Debug.WriteLine("Debug 1");

                bool accumulatorChanged = false;
                while (true) {
                    int characterCode = _processReader.Read();

                    if (characterCode == -1) {
                        Debug.WriteLine("Debug A");
                        
                        break;
                    }
                    
                    Debug.WriteLine("Debug B, " + characterCode);

                    char character = (char)characterCode;

                    accumulator += character;
                    accumulatorChanged = true;

                    Debug.WriteLine("Debug 2");
                }
                
                Debug.WriteLine("Debug 3");

                if (_logProcessOutput && accumulatorChanged) {
                    Debug.WriteLine("\n-------- PROCESS START --------");
                    Debug.WriteLine(accumulator);
                    Debug.WriteLine("------- PROCESS CURRENT -------\n");
                }

                _readerMutex.WaitOne();

                foreach (CMDCallback callback in _callbacks) {
                    MatchCollection matches = callback.Trigger.Matches(accumulator);
                    
                    if (matches.Count <= callback.MatchesFound) {
                        continue;
                    }

                    int matchesLengthDelta = matches.Count - callback.MatchesFound;
                    IEnumerable<Match> matchesDelta = matches.Skip(Math.Max(0, matches.Count - matchesLengthDelta));
                    
                    foreach (Match match in matchesDelta) { 
                        if (match.Success) {
                            mainThreadContext.Post(_ => {
                                callback.Callback.Invoke(match);
                            }, null);
                        }
                    }
                    
                    callback.MatchesFound = matches.Count;
                }
                
                _readerMutex.ReleaseMutex();
            }
        });
    }

    public void Destroy() {
        _threadStateMutex.WaitOne();
        
        closeThread = true;
        
        _threadStateMutex.ReleaseMutex();
        
        _processWriter.Close();
        _processReader.Close();
        _process.Close();
    }
    
    private void OnProcessExited(object? sender, EventArgs args) {
        Debug.WriteLine("Process Exited");
        
        Destroy();
    }
}