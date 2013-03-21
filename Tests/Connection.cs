﻿using BookSleeve;
using NUnit.Framework;
using System.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Tests
{
    [TestFixture]
    public class Connections // http://redis.io/commands#connection
    {
        [Test]
        public void TestConnectViaSentinel()
        {
            string[] endpoints;
            StringWriter sw = new StringWriter();
            var selected = ConnectionUtils.SelectConfiguration("192.168.0.19:26379,serviceName=mymaster", out endpoints, sw);
            string log = sw.ToString();
            Console.WriteLine(log);
            Assert.IsNotNull(selected, NO_SERVER);
            Assert.AreEqual("192.168.0.19:6379", selected);
        }
        [Test]
        public void TestConnectViaSentinelInvalidServiceName()
        {
            string[] endpoints;
            StringWriter sw = new StringWriter();
            var selected = ConnectionUtils.SelectConfiguration("192.168.0.19:26379,serviceName=garbage", out endpoints, sw);
            string log = sw.ToString();
            Console.WriteLine(log);
            Assert.IsNull(selected);
        }

        const string NO_SERVER = "No server available";
        [Test]
        public void TestDirectConnect()
        {
            string[] endpoints;
            StringWriter sw = new StringWriter();
            var selected = ConnectionUtils.SelectConfiguration("192.168.0.19:6379", out endpoints, sw);
            string log = sw.ToString();
            Console.WriteLine(log);
            Assert.IsNotNull(selected, NO_SERVER);
            Assert.AreEqual("192.168.0.19:6379", selected);

        }

        [Test]
        public void TestName()
        {
            using (var conn = Config.GetUnsecuredConnection(open: false, allowAdmin: true))
            {
                string name = Guid.NewGuid().ToString().Replace("-","");
                conn.Name = name;
                conn.Wait(conn.Open());
                if (conn.Features.ClientName)
                {
                    var client = conn.Wait(conn.Server.ListClients()).SingleOrDefault(c => c.Name == name);
                    Assert.IsNotNull(client);
                }
            }
        }

        [Test]
        public void TestSubscriberName()
        {
            using (var conn = Config.GetUnsecuredConnection(open: false, allowAdmin: true))
            {
                string name = Guid.NewGuid().ToString().Replace("-", "");
                conn.Name = name;
                conn.Wait(conn.Open());
                if (conn.Features.ClientName)
                {
                    using (var subscriber = conn.GetOpenSubscriberChannel())
                    {
                        var evt = new ManualResetEvent(false);
                        var tmp =  subscriber.Subscribe("test-subscriber-name", delegate
                         {
                             evt.Set();
                         });
                        subscriber.Wait(tmp);
                        conn.Publish("test-subscriber-name", "foo");
                        Assert.IsTrue(evt.WaitOne(1000), "event was set");
                        var clients = conn.Wait(conn.Server.ListClients()).Where(c => c.Name == name).ToList();
                        Assert.AreEqual(2, clients.Count, "number of clients");
                    }
                }

            }
        }

        [Test]
        public void TestForcedSubscriberName()
        {
            using (var conn = Config.GetUnsecuredConnection(allowAdmin: true, open: true, waitForOpen: true))
            using (var sub = new RedisSubscriberConnection(conn.Host, conn.Port))
            {
                var task = sub.Subscribe("foo", delegate { });
                string name = Guid.NewGuid().ToString().Replace("-", "");
                sub.Name = name;
                sub.SetServerVersion(new Version("2.6.9"), ServerType.Master);
                sub.Wait(sub.Open());
                sub.Wait(task);
                Assert.AreEqual(1, sub.SubscriptionCount);

                if (conn.Features.ClientName)
                {
                    var clients = conn.Wait(conn.Server.ListClients()).Where(c => c.Name == name).ToList();
                    Assert.AreEqual(1, clients.Count, "number of clients");
                }
            }
        }

        [Test]
        public void TestNameViaConnect()
        {
            string name = Guid.NewGuid().ToString().Replace("-","");
            using (var conn = ConnectionUtils.Connect("192.168.0.10,allowAdmin=true,name=" + name))
            {
                Assert.IsNotNull(conn, NO_SERVER);
                Assert.AreEqual(name, conn.Name);
                if (conn.Features.ClientName)
                {
                    var client = conn.Wait(conn.Server.ListClients()).SingleOrDefault(c => c.Name == name);
                    Assert.IsNotNull(client);
                }
            }
        }

        // AUTH is already tested by secured connection

        // QUIT is implicit in dispose

        // ECHO has little utility in an application

        [Test]
        public void TestGetSetOnDifferentDbHasDifferentValues()
        {
            // note: we don't expose SELECT directly, but we can verify that we have different DBs in play:

            using (var conn = Config.GetUnsecuredConnection())
            {
                conn.Strings.Set(1, "select", "abc");
                conn.Strings.Set(2, "select", "def");
                var x = conn.Strings.GetString(1, "select");
                var y = conn.Strings.GetString(2, "select");
                conn.WaitAll(x, y);
                Assert.AreEqual("abc", x.Result);
                Assert.AreEqual("def", y.Result);
            }
        }
        [Test, ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void TestGetOnInvalidDbThrows()
        {
            using (var conn = Config.GetUnsecuredConnection())
            {
                conn.Strings.GetString(-1, "select");                
            }
        }


        [Test]
        public void Ping()
        {
            using (var conn = Config.GetUnsecuredConnection())
            {
                var ms = conn.Wait(conn.Server.Ping());
                Assert.GreaterOrEqual(ms, 0);
            }
        }

        [Test]
        public void CheckCounters()
        {
            using (var conn = Config.GetUnsecuredConnection(waitForOpen:true))
            {
                conn.Wait(conn.Strings.GetString(0, "CheckCounters"));
                var first = conn.GetCounters();

                conn.Wait(conn.Strings.GetString(0, "CheckCounters"));
                var second = conn.GetCounters();
                // +2 = ping + one select
                Assert.AreEqual(first.MessagesSent + 2, second.MessagesSent, "MessagesSent");
                Assert.AreEqual(first.MessagesReceived + 2, second.MessagesReceived, "MessagesReceived");
                Assert.AreEqual(0, second.ErrorMessages, "ErrorMessages");
                Assert.AreEqual(0, second.MessagesCancelled, "MessagesCancelled");
                Assert.AreEqual(0, second.SentQueue, "SentQueue");
                Assert.AreEqual(0, second.UnsentQueue, "UnsentQueue");
                Assert.AreEqual(0, second.QueueJumpers, "QueueJumpers");
                Assert.AreEqual(0, second.Timeouts, "Timeouts");
                Assert.IsTrue(second.Ping >= 0, "Ping");
                Assert.IsTrue(second.ToString().Length > 0, "ToString");
            }
        }

        
    }
}
