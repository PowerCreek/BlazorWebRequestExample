using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using basicblazorexamplewebapp.DBRepo;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharedAssemblies2;
using SharedAssemblies2.Models;
using Name = SharedAssemblies2.Models.Name;

namespace basicblazorexamplewebapp.Controllers
{
    public class HomeController : Controller
    {

        public static int[] Counter = {0};

        public static ConcurrentDictionary<string, int> HostMap = new();

        /// <summary>
        /// Guid to IndexGroup
        /// IndexGroup is which group of indexes are related
        /// </summary>
        public static ConcurrentDictionary<int, List<Guid>> IndexGroup = new();
        
        /// <summary>
        /// Guid to Index
        /// Index of the Guid representing what the row id is.
        /// </summary>
        public static ConcurrentDictionary<Guid, (int group, int id)> IndexMap = new();

        public void RemoveIndexGroup(int i)
        {
            IndexGroup.TryRemove(i, out var guids);
            guids.ForEach(e => IndexMap.Remove(e, out var any));
        }

        public List<Guid> SetupIndexGroups(params int[] vals)
        {
            int count;
            lock (Counter)
            {
                count = Counter[0];
                Counter[0]++;
            }

            string hostname = HttpContext.Connection.RemoteIpAddress.ToString();
            
            List<Guid> guidList = null;
            
            lock(HostMap){
                Console.WriteLine(hostname);
                
                if (!HostMap.TryAdd(hostname, count))
                    count = HostMap[hostname];

                if (!IndexGroup.TryAdd(count, guidList = new List<Guid>()))
                {
                    guidList = IndexGroup[count];
                }
            }

            foreach (var val in vals)
            {
                bool contained = false;
                
                foreach (var g in guidList)
                {
                    if (IndexMap[g].id == val)
                        contained = true;
                }

                if (contained) continue;
                
                Guid guid;
                guidList.Add(guid = Guid.NewGuid());
                IndexMap.TryAdd(guid, (count, val));
            }
            
            Console.WriteLine(string.Join(",", guidList));

            return guidList;
        }

        public void Transact(Action a = null)
        {
            a?.Invoke();
            HostMap.TryGetValue(HttpContext.Connection.RemoteIpAddress.ToString(), out int group);
            RemoveIndexGroup(group);
        }
        
        // public dynamic[] FetchItems<T>(IQueryable<T> ctx, int page, int interval)
        // {
        //     //need to create all of the indices to construct guids for every name.
        //     var hold = ctx.ToList();
        //
        //     var indices = hold.Select(e => (int)(e as dynamic).id).ToArray();
        //
        //     //need to setup the index groups and register guids.
        //     var items = SetupIndexGroups(indices);
        //
        //     //skip to only what is needed to sort through.
        //     hold = ctx.Skip(page * interval).Take(interval).ToList();
        //     
        //     return hold.Select((t, h) =>
        //     {
        //         Console.WriteLine((page*interval)+h);
        //         Console.WriteLine("item count: "+items.Count);
        //         return (dynamic) new
        //         {
        //             Guid = items[(page*interval)+h],
        //             Instance = t
        //         };
        //     }).ToArray();
        // }
        
        public dynamic[] FetchItems<T>(IQueryable<T> ctx, int page, int interval, Func<IQueryable<T>, IOrderedQueryable<T>> doThing = null)
        {
            //need to create all of the indices to construct guids for every name.
            IEnumerable<T> perform = doThing?.Invoke(ctx); 
            perform??=ctx;
            
            var hold = perform.ToList();

            var indices = hold.Select(e => (int)(e as dynamic).id).ToArray();

            //need to setup the index groups and register guids.
            var items = SetupIndexGroups(indices);

            //skip to only what is needed to sort through.
            hold = perform.Skip(page * interval).Take(interval).ToList();
            
            return hold.Select((t, h) =>
            {
                Console.WriteLine((page*interval)+h);
                return (dynamic) new
                {
                    Guid = items[(page*interval)+h],
                    Instance = t
                };
            }).ToArray();
        }
        
        public async Task OnNameAdd(Name name, [FromServices] MasterDBContext ctx)
        {
            await ctx.Names.AddAsync(name);
            await ctx.SaveChangesAsync();
            Transact();
        }
        
        public int GetHostGroup() => HostMap.ContainsKey(HttpContext.Connection.RemoteIpAddress.ToString()) ? HostMap[HttpContext.Connection.RemoteIpAddress.ToString()] : -1;

        public async Task<bool> RemoveNames(MasterDBContext ctx, string guid)
        {
            
            int hostgroup = GetHostGroup();
            if (hostgroup == -1)
            {
                return false;
            }
            var indexGroup = IndexGroup[hostgroup];

            var stuff = (IndexGroup.Select(g => (g.Value.SingleOrDefault(c => (c.ToString().Equals(guid)))))).FirstOrDefault();
                        
            if (stuff == default(Guid)) return false;

            IndexMap.TryRemove(stuff, out var things);
            indexGroup.Remove(stuff);

            Func<Name, bool> doStuff = (name) =>
            {
                bool matched = name.id == things.id;
                return matched;
            };
            
            var removed = ctx.Names.Remove(ctx.Names.Single(doStuff));
            
            await ctx.SaveChangesAsync();
            return true;
        }
        
        // Post
        [HttpPost("/namesadd")]
        public async Task<IActionResult> NamesAddPost([FromForm] Name name, [FromServices] MasterDBContext ctx) =>
            await OnNameAdd(name, ctx)
            .ContinueWith(e => Ok("test"));
        
        
        [HttpGet("/namesadd")]
        public async Task<IActionResult> NamesAddGet([FromQuery] Name name, [FromServices] MasterDBContext ctx) =>
            await OnNameAdd(name, ctx)
                .ContinueWith(e => Ok("test"));


        [HttpGet("/transact")]
        public async Task<string> DoTransact([FromServices] MasterDBContext ctx)
        {
            Transact();
            return "transact";
        }

        public Func<IQueryable<Name>, IOrderedQueryable<Name>> NameQuery = (names) => names.OrderBy(e => e.Value);
        

            [HttpGet("/fetchnames")]
        public async Task<string> GetFetchNames([FromServices] MasterDBContext ctx, [FromBody] string jpaging)
        {
            Console.WriteLine(jpaging);
            
            Paging paging = JsonConvert.DeserializeObject<Paging>(jpaging.ToString());
            
            Console.WriteLine(paging.Page +":"+ paging.Interval);
            
            return JsonConvert.SerializeObject(FetchItems(ctx.Names, paging.Page, paging.Interval, NameQuery).Select(e =>
                {
                    Console.WriteLine(e.Guid+":"+e.Instance.Value);
                    
                    return (GuidInstance) new()
                    {
                        Guid = (Guid) e.Guid,
                        Value = (string) e.Instance.Value,
                    };
                }
            ).ToArray());  
        } 


        [HttpPost("/fetchnames")]
        public async Task<string> PostFetchNames([FromServices] MasterDBContext ctx, [FromBody] string jpaging)
        {
            Console.WriteLine(jpaging);
            
            Paging paging = JsonConvert.DeserializeObject<Paging>(jpaging.ToString());
            
            Console.WriteLine(paging.Page +":"+ paging.Interval);
            
            return JsonConvert.SerializeObject(FetchItems(ctx.Names, paging.Page, paging.Interval, NameQuery).Select(e =>
                {
                    Console.WriteLine(e.Guid+":"+e.Instance.Value);
                    
                    return (GuidInstance) new()
                    {
                        Guid = (Guid) e.Guid,
                        Value = (string) e.Instance.Value,
                    };
                }
            ).ToArray());  
        } 

        
        [HttpGet("/removenames")]
        public async Task<string> GetRemoveNames([FromServices] MasterDBContext ctx, [FromQuery] string guid)
        => await RemoveNames(ctx, guid) ? "removed" : "not found";
        
        
        [HttpPost("/removenames")]
        public async Task<string> PostRemoveNames([FromServices] MasterDBContext ctx, [FromForm] string Guid)
        {
            Console.WriteLine("guid: "+Guid);
            return await RemoveNames(ctx, Guid) ? "removed" : "not found";
        }
    }
}