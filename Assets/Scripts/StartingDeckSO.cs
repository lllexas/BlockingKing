using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "StartingDeck", menuName = "BlockingKing/Cards/Starting Deck")]
public class StartingDeckSO : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        [AssetsOnly]
        public CardSO card;

        [Min(1)]
        public int count = 1;
    }

    [Title("Cards")]
    [TableList(AlwaysExpanded = true, DrawScrollView = true, MinScrollViewHeight = 120)]
    public List<Entry> cards = new List<Entry>();
}
