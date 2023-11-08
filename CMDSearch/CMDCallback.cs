using System.Text.RegularExpressions;

namespace CMDSearch; 

public class CMDCallback {
    public int MatchesFound;
    public Regex Trigger;
    public Action<Match> Callback;

    public CMDCallback(int matchesFound, Regex trigger, Action<Match> callback) {
        MatchesFound = matchesFound;
        Trigger = trigger;
        Callback = callback;
    }
}