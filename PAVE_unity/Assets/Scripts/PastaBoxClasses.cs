using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PastaBoxClasses
{
    [System.Serializable]
    public class FailRun
    {
        public int run;
        public int mov;
        public float perc;
        public bool started = false;
    }

    [System.Serializable]
    public class Cell
    {
        public int cell_id;
        public string cell_name;
        public string failure;
        public bool delay;
        public int color_id;
        public List<FailRun> failrun;
    }

    [System.Serializable]
    public class Participant
    {
        public int id;
        public List<Cell> cells;
    }

    [System.Serializable]
    public class ParticipantList
    {
        public List<Participant> participants;
    }
}
