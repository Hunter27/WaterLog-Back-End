﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EmailNotifications;
using WaterLog_Backend.Controllers;
using WaterLog_Backend.Models;

using WebApplication1;
using NUnit.Framework;

namespace WaterLog_Backend
{
    public class Procedures
    {
        DatabaseContext _db;
        IConfiguration _config;
        public Procedures(DatabaseContext db,IConfiguration cfg)
        {
            _db = db;
            _config = cfg;
        }

        public async Task triggerInsert(ReadingsEntry value)
        {
            IEnumerable<SegmentsEntry> allSegments = _db.Segments;
            int segmentInid = -1;
            int segmentOutid = -1;
            int segmentid = -1;
            foreach (SegmentsEntry seg in allSegments)
            {
                if (seg.SenseIDIn == value.MonitorsId)
                {
                    segmentInid = seg.SenseIDIn;
                    segmentOutid = seg.SenseIDOut;
                    segmentid = seg.Id;
                    break;
                }
            }

            IEnumerable<ReadingsEntry> allReadings = _db.Readings;
            allReadings = allReadings.OrderByDescending(read => read.TimesStamp);
            ReadingsEntry r1 = null, r2 = null;
            Boolean found1 = false, found2 = false;
            foreach (ReadingsEntry read in allReadings)
            {
                if (found1 && found2)
                {
                    break;
                }
                if (read.MonitorsId == segmentInid && found1 == false)
                {
                    r1 = read;
                    found1 = true;
                }
                if (read.MonitorsId == segmentOutid && found2 == false)
                {
                    r2 = read;
                    found2 = true;
                }
            }

            if (isLeakage(r1.Value, r2.Value))
            {
                //Updateleakagestatus
                IEnumerable<SegmentLeaksEntry> allLeaks = _db.SegmentLeaks;
                if (allLeaks.Any(leak => leak.SegmentsId == segmentid))
                {
                    SegmentLeaksEntry latestEntry = allLeaks.Where(leak => leak.SegmentsId == segmentid).Last();
                    //Check in SegmentEntry if latest event related to entry has been resolved.
                    if (latestEntry != null)
                    {
                        IEnumerable<SegmentEventsEntry> allEvents = _db.SegmentEvents;
                        SegmentEventsEntry entry = allEvents.Where(leak => leak.SegmentsId == segmentid).Last();
                        if (entry.EventType == "leak")
                        {
                            await updateSegmentLeaksAsync(latestEntry.Id, segmentid, calculateSeverity(segmentid), latestEntry.OriginalTimeStamp, entry.TimeStamp, "unresolved");
                        }
                    }
                }
                else
                {
                    //Normal Add
                    await createSegmentLeaksAsync(segmentid, calculateSeverity(segmentid), "unresolved");
                    string[] template = populateEmail(segmentid);
                    Email email = new Email(template,_config);
                    email.sendEmail();
                }
            }
            else
            {
                //Updatewithoutleakagestatus
                await updateSegmentsEventAsync(segmentid, "normal", r1.Value, r2.Value);
            }
        }

        

        private string calculateSeverity(int segmentid)
        {
            return "severe";
        }

        public async Task createSegmentLeaksAsync(int segId, string severity, string resolvedStatus)
        {
            SegmentLeaksEntry entry = new SegmentLeaksEntry();
            entry.SegmentsId = segId;
            entry.Severity = severity;
            entry.LatestTimeStamp = DateTime.UtcNow;
            entry.OriginalTimeStamp = DateTime.UtcNow;
            entry.ResolvedStatus = resolvedStatus;
            await _db.SegmentLeaks.AddAsync(entry);
            await _db.SaveChangesAsync();
        }

        public async Task updateSegmentLeaksAsync(int leakId, int segId, string severity, DateTime original, DateTime updated, string resolvedStatus)
        {
            //SegmentLeaksController controller = getSegmentLeaksController();
            SegmentLeaksEntry entry = new SegmentLeaksEntry();
            entry.SegmentsId = segId;
            entry.Severity = severity;
            entry.OriginalTimeStamp = original;
            entry.LatestTimeStamp = updated;
            entry.ResolvedStatus = resolvedStatus;
            entry.Id = leakId;
            await _db.SegmentLeaks.AddAsync(entry);
            await _db.SaveChangesAsync();
        }

        public async Task updateSegmentsEventAsync(int id, string status, double inv, double outv)
        {
            SegmentEventsEntry entry = new SegmentEventsEntry();
            entry.TimeStamp = DateTime.UtcNow;
            entry.SegmentsId = id;
            entry.FlowIn = inv;
            entry.FlowOut = outv;
            entry.EventType = status;
            await _db.SegmentEvents.AddAsync(entry);
            await _db.SaveChangesAsync();
        }


        public Boolean isLeakage(double first, double second)
        {
            double margin = 2;
            if ((first - second) > margin)
            {
                return true;
            }
            return false;
        }

        public string[] populateEmail(int sectionid)
        {
            var leaks = _db.SegmentLeaks;
            var leak = leaks.Where(sudo => sudo.SegmentsId == sectionid).Single();
            string[] template = { "Segment " + leak.SegmentsId, getSegmentStatus(leak.SegmentsId), leak.Severity, getLeakPeriod(leak), calculateTotalCost(leak).ToString(), calculatePerHourCost(leak).ToString(), calculateLitresPerHour(leak).ToString(), buildUrl(leak.SegmentsId) };
            return template;
        }

        private string getLeakPeriod(SegmentLeaksEntry leak)
        {

            if (((leak.LatestTimeStamp - leak.OriginalTimeStamp).TotalHours) < 1)
            {
                return "1";
            }
            else
            {
                return ((leak.LatestTimeStamp - leak.OriginalTimeStamp).TotalDays.ToString());
            }
        }

        public double calculateTotalCost(SegmentLeaksEntry leak)
        {
            var list = _db.SegmentEvents;
            var entry = list.Where(inlist => inlist.SegmentsId == leak.SegmentsId).Last();
            var timebetween = (leak.LatestTimeStamp - leak.OriginalTimeStamp).TotalHours;
            if (timebetween < 1)
            {
                return calculatePerHourCost(leak)/60;
            }
            else
            {
                timebetween = (leak.LatestTimeStamp - leak.OriginalTimeStamp).TotalHours;
            }
            var perhour = calculatePerHourCost(leak);
            return (timebetween * perhour);
        }

        private string buildUrl(int segmentId)
        {
            return "https://iot.retrotest.co.za/alert/segment/" + segmentId;
        }

        public double calculatePerHourCost(SegmentLeaksEntry leak)
        {
            var list = _db.SegmentEvents;
            var entry = list.Where(inlist => inlist.SegmentsId == leak.SegmentsId).Last();
            double currentTariff = 37.5;
            double usageperpoll = (entry.FlowIn - entry.FlowOut);
            return (usageperpoll * currentTariff);
        }

        public double calculateLitresPerHour(SegmentLeaksEntry leak)
        {
            var list = _db.SegmentEvents;
            var entry = list.Where(inlist => inlist.SegmentsId == leak.SegmentsId).Last();
            double usageperpoll = (entry.FlowIn - entry.FlowOut);
            return (usageperpoll);
        }

        public double calculateTotaLitres(SegmentLeaksEntry leak)
        {
            var list = _db.SegmentEvents;
            var entry = list.Where(inlist => inlist.SegmentsId == leak.SegmentsId).Last();
            var timebetween = (leak.LatestTimeStamp - leak.OriginalTimeStamp).TotalHours;
            var perhour = calculateLitresPerHour(leak);
            return (timebetween * perhour);
        }

        private string getSegmentStatus(int segmentId)
        {
            var list = _db.SegmentEvents;
            var entry = list.Where(inlist => inlist.SegmentsId == segmentId).Last();
            return entry.EventType;
        }

        //Calculates the data points of the wastage based on period
        public DataPoints<DateTime,double>[] CalculatePeriodWastage(Period timeframe)
        {
            switch (timeframe)
            {
                case Period.Daily:
                    return CalculateDailyWastage(_db.SegmentEvents.Where(a => a.EventType == "leak" && a.TimeStamp.Month == DateTime.Now.Month && a.TimeStamp.Day == DateTime.Now.Day && a.TimeStamp.Year == DateTime.Now.Year).GroupBy(b => b.TimeStamp.Hour).ToList());
                case Period.Monthly:
                    return (CalculateMonthlyWastage(_db.SegmentEvents.Where(a => a.EventType == "leak" && a.TimeStamp.Month == DateTime.Now.Month && a.TimeStamp.Day == DateTime.Now.Day && a.TimeStamp.Year == DateTime.Now.Year).GroupBy(b => b.TimeStamp.Day).ToList()));
                case Period.Seasonally:
                    DateTime summerBegin = new DateTime(0, 12, 1);
                    DateTime summerEnd = new DateTime(0, 2, 28);
                    DateTime winterBegin = new DateTime(0, 6, 1);
                    DateTime winterEnd = new DateTime(0, 8, 31);
                    DateTime autumnBegin = new DateTime(0, 3, 1);
                    DateTime autumnEnd = new DateTime(0, 5, 31);
                    DateTime springBegin = new DateTime(0, 9, 1);
                    DateTime springEnd = new DateTime(0, 11, 30);
                    return CalculateSeasonallyWastage(_db.SegmentEvents.Where(a => a.EventType == "leak" && a.TimeStamp.Month >= summerBegin.Month && a.TimeStamp.Day >= summerBegin.Day && a.TimeStamp.Month <= summerEnd.Month && a.TimeStamp.Day <= summerEnd.Day).ToList(), _db.SegmentEvents.Where(a => a.EventType == "leak" && a.TimeStamp.Month >= winterBegin.Month && a.TimeStamp.Day >= winterBegin.Day && a.TimeStamp.Month <= winterEnd.Month && a.TimeStamp.Day <= winterEnd.Day).ToList(), _db.SegmentEvents.Where(a => a.EventType == "leak" && a.TimeStamp.Month >= autumnBegin.Month && a.TimeStamp.Day >= autumnBegin.Day && a.TimeStamp.Month <= autumnEnd.Month && a.TimeStamp.Day <= autumnEnd.Day).ToList(), _db.SegmentEvents.Where(a => a.EventType == "leak" && a.TimeStamp.Month >= springBegin.Month && a.TimeStamp.Day >= springBegin.Day && a.TimeStamp.Month <= springEnd.Month && a.TimeStamp.Day <= springEnd.Day).ToList());
                default:
                    return null;
            }
        }

        //Returns an array of yearly sorted data
        // 0 - summer
        // 1 - winter
        // 2 - spring
        // 3 - autumn
        public DataPoints<DateTime, double>[] CalculateSeasonallyWastage(List<SegmentEventsEntry> summer, List<SegmentEventsEntry> winter, List<SegmentEventsEntry> autumn, List<SegmentEventsEntry> spring)
        {
            //Get Summer
            var sortedSummer = CalculateYearlyWastage(summer.GroupBy(a => a.TimeStamp.Month).ToList());
            //Get Winter
            var sortedWinter = CalculateYearlyWastage(winter.GroupBy(a => a.TimeStamp.Month).ToList());
            //Get Autumn
            var sortedAutumn = CalculateYearlyWastage(autumn.GroupBy(a => a.TimeStamp.Month).ToList());
            //Get Spring
            var sortedSpring = CalculateYearlyWastage(spring.GroupBy(a => a.TimeStamp.Month).ToList());

            DataPoints<DateTime, double>[] arrayOfSeasons = new DataPoints<DateTime, double>[4];

            arrayOfSeasons[0] = sortedSummer[0];
            arrayOfSeasons[1] = sortedWinter[0];
            arrayOfSeasons[2] = sortedSpring[0];
            arrayOfSeasons[3] = sortedAutumn[0];

            return arrayOfSeasons;
        }

        public DataPoints<DateTime, double>[] CalculateYearlyWastage(List<IGrouping<int, SegmentEventsEntry>> list)
        {
            DataPoints<DateTime, double> yearly = new DataPoints<DateTime, double>();
            var totalForMonth = 0.0;
            for (int i = 0; i < list.Count; i++)
            {
                //We have a list per hour of current day.
                //Group these groups by segmentId
                totalForMonth = 0.0;
                var segments = list.ElementAt(i).GroupBy(a => a.SegmentsId);
                foreach (IGrouping<int, SegmentEventsEntry> lst in segments)
                {
                    foreach (SegmentEventsEntry lst2 in lst)
                    {
                        totalForMonth += ((lst2.FlowIn - lst2.FlowOut) / 60);

                    }

                }
                yearly.AddPoint(list.ElementAt(i).ElementAt(0).TimeStamp, totalForMonth);
            }
            DataPoints<DateTime, double>[] ret = new DataPoints<DateTime, double>[1];
            
            ret[0] = yearly;
            return ret;
        }

        public DataPoints<DateTime,double>[] CalculateMonthlyWastage(List<IGrouping<int,SegmentEventsEntry>> list)
        {
            DataPoints<DateTime, double> monthly = new DataPoints<DateTime, double>();
            var totalForDay = 0.0;
            for (int i = 0; i < list.Count; i++)
            {
                //We have a list per hour of current day.
                //Group these groups by segmentId
                totalForDay = 0.0;
                var segments = list.ElementAt(i).GroupBy(a => a.SegmentsId);
                foreach (IGrouping<int, SegmentEventsEntry> lst in segments)
                {
                    foreach (SegmentEventsEntry lst2 in lst)
                    {
                        totalForDay += ((lst2.FlowIn - lst2.FlowOut) / 60);

                    }

                }
                monthly.AddPoint(list.ElementAt(i).ElementAt(0).TimeStamp, totalForDay);
            }
            DataPoints<DateTime, double>[] ret = new DataPoints<DateTime, double>[1];
            ret[0] = monthly;
            return ret;
        }

        public DataPoints<DateTime,double>[] CalculateDailyWastage(List<IGrouping<int,SegmentEventsEntry>> list)
        {
            DataPoints<DateTime, double> daily = new DataPoints<DateTime, double>();
            var totalForHour = 0.0;
            for (int i = 0; i < list.Count; i++)
            {
                //We have a list per hour of current day.
                //Group these groups by segmentId
                totalForHour = 0.0;
                var segments = list.ElementAt(i).GroupBy(a => a.SegmentsId);
                foreach(IGrouping<int,SegmentEventsEntry> lst in segments)
                {
                    foreach(SegmentEventsEntry lst2 in lst)
                    {
                        totalForHour += ((lst2.FlowIn - lst2.FlowOut) / 60);
                       
                    }

                }
                daily.AddPoint(list.ElementAt(i).ElementAt(0).TimeStamp, totalForHour);
            }
            DataPoints<DateTime, double>[] ret = new DataPoints<DateTime, double>[1];
            ret[0] = daily;
            return ret;
        }

        public enum Period
        {
            Daily,
            Seasonally,
            Monthly
        }
    }
}
