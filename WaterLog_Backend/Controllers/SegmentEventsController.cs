﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using WaterLog_Backend.Models;

namespace WaterLog_Backend.Controllers
{

    [Route("api/[controller]")]
    [ApiController]
    public class SegmentEventsController : ControllerBase
    {
        private readonly DatabaseContext _db;
        readonly IConfiguration _config;
        public SegmentEventsController(DatabaseContext context, IConfiguration config)
        {
            _db = context;
            _config = config;
        }

        // GET api/values
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SegmentEventsEntry>>> Get()
        {

            return await _db.SegmentEvents.ToListAsync();
        }

        // GET api/values/5
        [HttpGet("{id}")]
        public async Task<ActionResult<SegmentEventsEntry>> Get(int id)
        {
            return await _db.SegmentEvents.FindAsync(id);
        }
      
        // POST api/values
        [HttpPost]
        public async Task Post([FromBody] SegmentEventsEntry value)
        {
            await _db.SegmentEvents.AddAsync(value);
            await _db.SaveChangesAsync();

            ;
        }

        // PUT api/values/5
        [HttpPut("{id}")]
        public async Task Put(int id, [FromBody] SegmentEventsEntry value)
        {
            var entry = await _db.SegmentEvents.FindAsync(id);
            entry = value;
            await _db.SaveChangesAsync();
        }

        // DELETE api/values/5
        [HttpDelete("{id}")]
        public async Task Delete(int id)
        {
            var entry = await _db.SegmentEvents.FindAsync(id);
            _db.SegmentEvents.Remove(entry);
            await _db.SaveChangesAsync();
        }
    }
}