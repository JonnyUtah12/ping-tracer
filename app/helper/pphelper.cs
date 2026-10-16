// Probe helper: reads "id host ttl timeoutMs size" lines on stdin, writes "id status ip rttMs" lines on stdout.
// Uses System.Net.NetworkInformation.Ping (IcmpSendEcho) so TTL-expired replies carry real RTTs.
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
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
                    byte[] salt = System.Text.Encoding.UTF8.GetBytes("PongPlotter.v1");
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
            if (p.Length >= 5 && (p[0] == "T" || p[0] == "U"))
            { // T|U id host port timeoutMs [size]  -> "id ok|timeout|err ip ms"
                int pt, to, sz = 32;
                if (int.TryParse(p[3], out pt) && int.TryParse(p[4], out to))
                {
                    if (p.Length > 5) int.TryParse(p[5], out sz);
                    System.Threading.Interlocked.Increment(ref pending);
                    if (p[0] == "T") ProbeTcp(o, p[1], p[2], pt, to); else ProbeUdp(o, p[1], p[2], pt, to, sz);
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
    // TCP "ping": a completed handshake OR a refusal (RST) both prove the host answered; timeout = loss.
    static async void ProbeTcp(StreamWriter o, string id, string host, int port, int timeout)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using (var c = new System.Net.Sockets.TcpClient())
            {
                var t = c.ConnectAsync(host, port);
                var done = await Task.WhenAny(t, Task.Delay(timeout));
                sw.Stop();
                if (done != t) { Reply(o, id, "timeout", "-", 0); return; }
                if (t.IsFaulted)
                {
                    var se = t.Exception.GetBaseException() as System.Net.Sockets.SocketException;
                    if (se != null && se.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionRefused) { Reply(o, id, "ok", host, Math.Max(0.1, Math.Round(sw.Elapsed.TotalMilliseconds, 1))); return; }
                    Reply(o, id, "timeout", "-", 0); return;
                }
                Reply(o, id, "ok", host, Math.Max(0.1, Math.Round(sw.Elapsed.TotalMilliseconds, 1)));
            }
        }
        catch (Exception) { Reply(o, id, "err", "-", 0); }
    }
    // UDP probe: a datagram reply, or the ICMP port-unreachable Windows surfaces as ConnectionReset, proves the host is up.
    static async void ProbeUdp(StreamWriter o, string id, string host, int port, int timeout, int size)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using (var u = new System.Net.Sockets.UdpClient())
            {
                u.Connect(host, port);
                var buf = new byte[Math.Max(1, Math.Min(size, 1400))];
                await u.SendAsync(buf, buf.Length);
                var r = u.ReceiveAsync();
                var done = await Task.WhenAny(r, Task.Delay(timeout));
                sw.Stop();
                if (done != r) { Reply(o, id, "timeout", "-", 0); return; }
                if (r.IsFaulted)
                {
                    var se = r.Exception.GetBaseException() as System.Net.Sockets.SocketException;
                    if (se != null && se.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionReset) { Reply(o, id, "ok", host, Math.Max(0.1, Math.Round(sw.Elapsed.TotalMilliseconds, 1))); return; }
                    Reply(o, id, "timeout", "-", 0); return;
                }
                Reply(o, id, "ok", host, Math.Max(0.1, Math.Round(sw.Elapsed.TotalMilliseconds, 1)));
            }
        }
        catch (System.Net.Sockets.SocketException se)
        {
            if (se.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionReset) Reply(o, id, "ok", host, Math.Max(0.1, Math.Round(sw.Elapsed.TotalMilliseconds, 1)));
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

