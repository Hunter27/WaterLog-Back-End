﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace WaterLog_Backend.Models
{
    public class SegmentEventsEntry
    {
        public int Id { get; set; }
        public int SegmentId { get; set; }

        [ForeignKey("SegmentId")]
        public SegmentsEntry SegmentsEntry { get; set; }

        public string EventType { get; set; }
        public DateTime TimeStamp { get; set; }
        public double Value { get; set; }

       
    }
}
