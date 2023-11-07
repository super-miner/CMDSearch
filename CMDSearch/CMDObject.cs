using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CMDSearch;

public class CMDObject {
    private string _batchFilePath;
    private Process _process;
    private StreamReader _processReader;
    private StreamWriter _processWriter;
    private Task _readerTask;

    private Mutex _readerMutex = new Mutex();
    private List<CMDCallback> _callbacks = new List<CMDCallback>();

    public CMDObject(string batchFilePath, string[] args, bool createWindow = false) {
        _batchFilePath = batchFilePath;

        string command = $"\"{batchFilePath}\" {string.Join(' ', args)}";
        ProcessStartInfo processInfo = new ProcessStartInfo("cmd.exe", $"/c {command}");
        processInfo.CreateNoWindow = !createWindow;
        processInfo.UseShellExecute = false;
        processInfo.RedirectStandardInput = true;
        processInfo.RedirectStandardOutput = true;

        _process = Process.Start(processInfo) ?? throw new NullReferenceException();
        
        _process.Exited += OnProcessExited;

        _processReader = _process.StandardOutput;
        _processWriter = _process.StandardInput;

        WatchStream();
    }

    public void AddCallback(string trigger, Action<Match> callback) {
        AddCallback(new Regex(trigger), callback);
    }

    public void AddCallback(Regex trigger, Action<Match> callback) {
        _readerMutex.WaitOne();
        
        _callbacks.Add(new CMDCallback() {
            Callback = callback,
            MatchesFound = 0,
            Trigger = trigger
        });
        
        _readerMutex.ReleaseMutex();
    }

    public void WriteInput(string input) {
        _processWriter.WriteLine(input);
    }
    
    private void WatchStream() {
        _readerTask = Task.Run(() => {
            string accumulator = "";
            
            while (true) {
                int characterCode = _processReader.Read();

                if (characterCode == -1) {
                    continue;
                }

                char character = (char)characterCode;

                accumulator += character;

                _readerMutex.WaitOne();

                foreach (CMDCallback callback in _callbacks) {
                    MatchCollection matches = callback.Trigger.Matches(accumulator);

                    if (matches.Count <= callback.MatchesFound) {
                        continue;
                    }

                    int matchesLengthDelta = matches.Count - callback.MatchesFound;
                    MatchCollection matchesDelta = (MatchCollection) matches.Skip(Math.Max(0, matches.Count - matchesLengthDelta));

                    foreach (Match match in matchesDelta) {
                        callback.Callback.Invoke(match);
                    }
                }
                
                _readerMutex.ReleaseMutex();
            }
        });
    }

    public void Destroy() {
        _readerTask.Dispose();
        
        _processWriter.Close();
        _processReader.Close();
        _process.Close();
    }
    
    private void OnProcessExited(object? sender, EventArgs args) {
        Destroy();
    }
}