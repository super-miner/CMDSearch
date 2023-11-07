using System.Text.RegularExpressions;

namespace CMDSearch; 

public struct CMDCallback {
    public int MatchesFound;
    public Regex Trigger;
    public Action<Match> Callback;
}