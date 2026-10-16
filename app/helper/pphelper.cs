// Probe helper: reads "id host ttl timeoutMs size" lines on stdin, writes "id status ip rttMs" lines on stdout.
// Uses System.Net.NetworkInformation.Ping (IcmpSendEcho) so TTL-expired replies carry real RTTs.
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Security.Cryptography;

static class PPHelper
{
    static readonly object Gate = new object();
    static int pending = 0;
    static void Main()
    {
        var o = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        string line;
        while ((line = Console.In.ReadLine()) != null)
        {
            var p = line.Split(' ');
            if (p.Length == 3 && (p[0] == "E" || p[0] == "X"))
            { // E|X id base64 -> "S id base64|!": encrypt / decrypt a secret with Windows DPAPI (bound to the current user account)
                string res;
                try
                {
                    byte[] data = Convert.FromBase64String(p[2]);
                    byte[] salt = System.Text.Encoding.UTF8.GetBytes("PingTracer.v1");
                    byte[] r = p[0] == "E" ? ProtectedData.Protect(data, salt, DataProtectionScope.CurrentUser) : ProtectedData.Unprotect(data, salt, DataProtectionScope.CurrentUser);
                    res = "S " + p[1] + " " + Convert.ToBase64String(r);
                }
                catch (Exception) { res = "S " + p[1] + " !"; }
                lock (Gate) o.WriteLine(res);
                continue;
            }
            if (p.Length == 11 && p[0] == "M")
            { // M id host port ssl user pass from to subject body  (every field base64 UTF-8)
                System.Threading.Interlocked.Increment(ref pending);
                Mail(o, p);
                continue;
            }
            if (p.Length >= 6 && (p[0] == "T" || p[0] == "U"))
            { // T|U id host port timeoutMs ttl [size]  -> "id ok|refused|timeout|unreach|err ip ms"
              // TTL-limited probes: the protocol itself discovers the path length (ttl counts up until the destination answers).
              // Windows user-mode sockets cannot report which router sent an ICMP Time Exceeded, so intermediate hop addresses
              // come from the engine's ICMP probes; the protocol probe proves reachability at each TTL.
                int pt, to, ttl, sz = 32;
                if (int.TryParse(p[3], out pt) && int.TryParse(p[4], out to) && int.TryParse(p[5], out ttl))
                {
                    if (p.Length > 6) int.TryParse(p[6], out sz);
                    System.Threading.Interlocked.Increment(ref pending);
                    if (p[0] == "T") ProbeTcp(o, p[1], p[2], pt, to, ttl); else ProbeUdp(o, p[1], p[2], pt, to, ttl, sz);
                }
                continue;
            }
            if (p.Length < 4) continue;
            string id = p[0], host = p[1];
            int ttl, timeout, size = 32;
            if (!int.TryParse(p[2], out ttl) || !int.TryParse(p[3], out timeout)) continue;
            if (p.Length > 4) int.TryParse(p[4], out size);
            System.Threading.Interlocked.Increment(ref pending);
            Probe(o, id, host, ttl, timeout, size);
        }
        while (System.Threading.Volatile.Read(ref pending) > 0) System.Threading.Thread.Sleep(20);
    }
    static string D(string b) { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b)); }
    static async void Mail(StreamWriter o, string[] p)
    {
        string res;
        try
        {
            string id = p[1], host = D(p[2]), user = D(p[5]), pass = D(p[6]), from = D(p[7]), to = D(p[8]), subj = D(p[9]), body = D(p[10]);
            int port = int.Parse(D(p[3])); bool ssl = D(p[4]) == "1";
            using (var c = new System.Net.Mail.SmtpClient(host, port))
            {
                c.EnableSsl = ssl; c.Timeout = 15000;
                if (user != "") c.Credentials = new NetworkCredential(user, pass);
                var m = new System.Net.Mail.MailMessage(from, to.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' '), subj.Replace('\r', ' ').Replace('\n', ' '), body);
                await c.SendMailAsync(m);
            }
            res = "M " + id + " ok";
        }
        catch (Exception e) { res = "M " + p[1] + " err " + System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(e.Message)); }
        lock (Gate) o.WriteLine(res);
        System.Threading.Interlocked.Decrement(ref pending);
    }
    static void Reply(StreamWriter o, string id, string st, string ip, double ms)
    {
        lock (Gate) o.WriteLine(id + " " + st + " " + ip + " " + ms.ToString(System.Globalization.CultureInfo.InvariantCulture));
        System.Threading.Interlocked.Decrement(ref pending);
    }
    static void SetTtl(Socket s, AddressFamily fam, int ttl)
    { // per-probe TTL: IPv4 IP_TTL (SocketOptionName.IpTimeToLive = 4), IPv6 unicast hop limit
      // (SocketOptionName.HopLimit = 21; kept as a numeric cast so it compiles against every reference
      // assembly - 27 is IPv6Only, not the hop limit). Options are set before connect/send; a failure
      // is swallowed so the probe still runs with the default TTL rather than reporting an error.
        if (ttl <= 0) return;
        try
        {
            if (fam == AddressFamily.InterNetwork) s.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.IpTimeToLive, ttl);
            else if (fam == AddressFamily.InterNetworkV6) s.SetSocketOption(SocketOptionLevel.IPv6, (SocketOptionName)21, ttl);
        }
        catch (Exception) { }
    }
    // TTL-limited TCP probe: a completed handshake (ok) or a refusal (refused, RST) both prove the destination
    // is reachable within that TTL; timeout = TTL expired en route, filtered port, or no response.
    static async void ProbeTcp(StreamWriter o, string id, string host, int port, int timeout, int ttl)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var addrs = Dns.GetHostAddresses(host);
            if (addrs.Length == 0) { Reply(o, id, "err", "-", 0); return; }
            var addr = Array.Find(addrs, a => a.AddressFamily == AddressFamily.InterNetwork) ?? addrs[0];
            using (var s = new Socket(addr.AddressFamily, SocketType.Stream, ProtocolType.Tcp))
            {
                SetTtl(s, addr.AddressFamily, ttl);
                var rem = new IPEndPoint(addr, port);
                var t = TaskFactory.FromAsync((cb, st) => s.BeginConnect(rem, cb, st), s.EndConnect, null);
                var done = await Task.WhenAny(t, Task.Delay(timeout));
                sw.Stop();
                double ms = Math.Max(0.1, Math.Round(sw.Elapsed.TotalMilliseconds, 1));
                if (done != t) { try { s.Close(); } catch { } Reply(o, id, "timeout", "-", 0); return; }
                if (t.IsFaulted)
                {
                    var se = t.Exception.GetBaseException() as SocketException;
                    if (se != null && se.SocketErrorCode == SocketError.ConnectionRefused) { Reply(o, id, "refused", addr.ToString(), ms); return; }
                    if (se != null && (se.SocketErrorCode == SocketError.NetworkUnreachable || se.SocketErrorCode == SocketError.HostUnreachable)) { Reply(o, id, "unreach", "-", 0); return; }
                    Reply(o, id, "timeout", "-", 0); return;
                }
                Reply(o, id, "ok", addr.ToString(), ms);
            }
        }
        catch (Exception) { Reply(o, id, "err", "-", 0); }
    }
    // TTL-limited UDP probe: a datagram reply (ok) or the ICMP Port Unreachable that Windows surfaces as
    // ConnectionReset (refused) both prove the destination is reachable within that TTL. No reply is never
    // proof of an offline destination - UDP probes are commonly firewalled.
    static async void ProbeUdp(StreamWriter o, string id, string host, int port, int timeout, int ttl, int size)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var addrs = Dns.GetHostAddresses(host);
            if (addrs.Length == 0) { Reply(o, id, "err", "-", 0); return; }
            var addr = Array.Find(addrs, a => a.AddressFamily == AddressFamily.InterNetwork) ?? addrs[0];
            using (var u = new UdpClient(addr.AddressFamily))
            {
                SetTtl(u.Client, addr.AddressFamily, ttl);
                u.Connect(addr, port);
                var buf = new byte[Math.Max(1, Math.Min(size, 1400))];
                await u.SendAsync(buf, buf.Length);
                var r = u.ReceiveAsync();
                var done = await Task.WhenAny(r, Task.Delay(timeout));
                sw.Stop();
                double ms = Math.Max(0.1, Math.Round(sw.Elapsed.TotalMilliseconds, 1));
                if (done != r) { Reply(o, id, "timeout", "-", 0); return; }
                if (r.IsFaulted)
                {
                    var se = r.Exception.GetBaseException() as SocketException;
                    if (se != null && se.SocketErrorCode == SocketError.ConnectionReset) { Reply(o, id, "refused", addr.ToString(), ms); return; }
                    Reply(o, id, "timeout", "-", 0); return;
                }
                Reply(o, id, "ok", addr.ToString(), ms);
            }
        }
        catch (SocketException se)
        {
            if (se.SocketErrorCode == SocketError.ConnectionReset) Reply(o, id, "refused", host, 0.1);
            else Reply(o, id, "err", "-", 0);
        }
        catch (Exception) { Reply(o, id, "err", "-", 0); }
    }
    static async void Probe(StreamWriter o, string id, string host, int ttl, int timeout, int size)
    {
        string res;
        try
        {
            using (var ping = new Ping())
            {
                var opts = new PingOptions(ttl, true);
                var buf = new byte[Math.Max(0, Math.Min(size, 65000))];
                var sw = Stopwatch.StartNew();
                var r = await ping.SendPingAsync(host, timeout, buf, opts);
                sw.Stop();
                double ms = Math.Max(0.1, Math.Round(sw.Elapsed.TotalMilliseconds, 1));
                string ip = r.Address != null ? r.Address.ToString() : "-";
                string st = r.Status == IPStatus.Success ? "ok" : (r.Status == IPStatus.TtlExpired || r.Status == IPStatus.TimeExceeded) ? "ttl" : r.Status == IPStatus.TimedOut ? "timeout" : "err";
                res = id + " " + st + " " + ip + " " + ms.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        catch (Exception) { res = id + " err - 0"; }
        lock (Gate) o.WriteLine(res);
        System.Threading.Interlocked.Decrement(ref pending);
    }
}

