using System;
using System.Collections.Generic;
using NekoGraph;
using UnityEngine;

[CreateAssetMenu(fileName = "RunMsg", menuName = "BlockingKing/Stage/Message")]
[VFSContentKind(VFSContentKind.UnityObject)]
public class RunMsgSO : ScriptableObject
{
    [Serializable]
    public sealed class Choice
    {
        public string choiceId;

        [TextArea(1, 2)]
        public string text;
    }

    public string messageId;
    public string title;
    public string speaker;

    [TextArea(4, 8)]
    public string body;

    public List<Choice> choices = new List<Choice>();
}
