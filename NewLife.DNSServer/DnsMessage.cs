#nullable disable
using System.Net;
using System.Text;
using NewLife.Net.DNS;

namespace NewLife.DNS.Server;

/// <summary>
/// 读写标准 DNS 报文。当前引用的 NewLife.Net 里 <see cref="DNSEntity.Read(Byte[], Boolean)"/> 固定返回 null，
/// 转发需要自己从原始报文取出问题，并在本地规则命中时把 <see cref="DNSEntity"/> 编回报文。
/// </summary>
public static class DnsMessage
{
    public static DNSEntity TryParse(Byte[] message)
    {
        if (message == null || message.Length < 12) return null;

        var entity = new DNSEntity();
        entity.Header.ID = (Int16)((message[0] << 8) | message[1]);
        entity.Header.Response = (message[2] & 0x80) != 0;
        var rcode = message[3] & 0x0F;
        if (Enum.IsDefined(typeof(DNSRcodeType), rcode))
            entity.Header.ResponseCode = (DNSRcodeType)rcode;

        var offset = 12;
        var questions = ReadQuestions(message, ref offset);
        if (questions == null || questions.Length == 0) return null;
        entity.Questions = questions;
        entity.Header.Questions = (Int16)questions.Length;

        var answerCount = (message[6] << 8) | message[7];
        var answers = new List<DNSRecord>();
        for (var i = 0; i < answerCount; i++)
        {
            var record = ReadRecord(message, ref offset);
            if (record == null) break;
            answers.Add(record);
        }
        if (answers.Count > 0)
        {
            entity.Answers = answers.ToArray();
            entity.Header.Answers = (Int16)answers.Count;
        }
        return entity;
    }

    /// <summary>用原始查询的问题段，加上实体里的回答，生成响应报文。</summary>
    public static Byte[] TryWrite(Byte[] query, DNSEntity response)
    {
        if (query == null || query.Length < 12 || response?.Answers == null || response.Answers.Length == 0) return null;
        var questionEnd = SkipQuestions(query);
        if (questionEnd < 12 || questionEnd > query.Length) return null;

        using var stream = new MemoryStream();
        stream.Write(query, 0, 2);
        stream.WriteByte(0x81);
        stream.WriteByte(0x80);
        stream.Write(query, 4, 2);
        Write16(stream, (UInt16)response.Answers.Length);
        Write16(stream, 0);
        Write16(stream, 0);
        stream.Write(query, 12, questionEnd - 12);
        foreach (var answer in response.Answers)
        {
            if (!WriteRecord(stream, answer)) return null;
        }
        return stream.ToArray();
    }

    static DNSQuery[] ReadQuestions(Byte[] message, ref Int32 offset)
    {
        var count = (message[4] << 8) | message[5];
        if (count <= 0) return null;
        var list = new DNSQuery[count];
        for (var i = 0; i < count; i++)
        {
            if (!TryReadName(message, ref offset, out var name)) return null;
            if (offset + 4 > message.Length) return null;
            var type = (message[offset] << 8) | message[offset + 1];
            var cls = (message[offset + 2] << 8) | message[offset + 3];
            offset += 4;
            list[i] = new DNSQuery
            {
                Name = name,
                Type = (DNSQueryType)type,
                Class = (DNSQueryClass)cls
            };
        }
        return list;
    }

    static DNSRecord ReadRecord(Byte[] message, ref Int32 offset)
    {
        if (!TryReadName(message, ref offset, out var name)) return null;
        if (offset + 10 > message.Length) return null;
        var type = (message[offset] << 8) | message[offset + 1];
        var ttl = (message[offset + 4] << 24) | (message[offset + 5] << 16) | (message[offset + 6] << 8) | message[offset + 7];
        var length = (message[offset + 8] << 8) | message[offset + 9];
        offset += 10;
        if (length < 0 || offset + length > message.Length) return null;
        var text = ReadRdata(message, offset, length, type);
        offset += length;
        if (text == null) return new DNSRecord { Name = name, Type = (DNSQueryType)type, TTL = TimeSpan.FromSeconds(ttl < 0 ? 0 : ttl) };

        var record = DNSEntity.CreateRecord((DNSQueryType)type) ?? new DNSRecord();
        record.Name = name;
        record.Type = (DNSQueryType)type;
        record.TTL = TimeSpan.FromSeconds(ttl < 0 ? 0 : ttl);
        record.Text = text;
        return record;
    }

    static String ReadRdata(Byte[] message, Int32 offset, Int32 length, Int32 type)
    {
        if (type == 1 && length == 4)
            return new IPAddress(new[] { message[offset], message[offset + 1], message[offset + 2], message[offset + 3] }).ToString();
        if (type == 28 && length == 16)
        {
            var bytes = new Byte[16];
            Buffer.BlockCopy(message, offset, bytes, 0, 16);
            return new IPAddress(bytes).ToString();
        }
        if (type == 2 || type == 5 || type == 12)
        {
            var cursor = offset;
            return TryReadName(message, ref cursor, out var name) ? name : null;
        }
        if (type == 16 && length > 0)
        {
            var parts = new List<String>();
            var end = offset + length;
            var cursor = offset;
            while (cursor < end)
            {
                var n = message[cursor++];
                if (cursor + n > end) return null;
                parts.Add(Encoding.ASCII.GetString(message, cursor, n));
                cursor += n;
            }
            return String.Join("", parts);
        }
        return null;
    }

    static Boolean WriteRecord(Stream stream, DNSRecord record)
    {
        if (record == null || String.IsNullOrEmpty(record.Text)) return false;
        WriteName(stream, String.IsNullOrEmpty(record.Name) ? "" : record.Name);
        var type = (UInt16)record.Type;
        Write16(stream, type == 0 ? (UInt16)1 : type);
        Write16(stream, 1);
        var ttl = (UInt32)Math.Max(0, (Int32)record.TTL.TotalSeconds);
        stream.WriteByte((Byte)(ttl >> 24));
        stream.WriteByte((Byte)(ttl >> 16));
        stream.WriteByte((Byte)(ttl >> 8));
        stream.WriteByte((Byte)ttl);

        var rdata = EncodeRdata(record);
        if (rdata == null) return false;
        Write16(stream, (UInt16)rdata.Length);
        stream.Write(rdata, 0, rdata.Length);
        return true;
    }

    static Byte[] EncodeRdata(DNSRecord record)
    {
        var type = (Int32)record.Type;
        if (type == 1 || type == 28)
        {
            if (!IPAddress.TryParse(record.Text, out var address)) return null;
            var bytes = address.GetAddressBytes();
            if (type == 1 && bytes.Length != 4) return null;
            if (type == 28 && bytes.Length != 16) return null;
            return bytes;
        }
        if (type == 2 || type == 5 || type == 12)
        {
            using var stream = new MemoryStream();
            WriteName(stream, record.Text);
            return stream.ToArray();
        }
        if (type == 16)
        {
            var text = Encoding.ASCII.GetBytes(record.Text);
            if (text.Length > 255) return null;
            var bytes = new Byte[text.Length + 1];
            bytes[0] = (Byte)text.Length;
            Buffer.BlockCopy(text, 0, bytes, 1, text.Length);
            return bytes;
        }
        return null;
    }

    static Int32 SkipQuestions(Byte[] message)
    {
        var count = (message[4] << 8) | message[5];
        var offset = 12;
        for (var i = 0; i < count; i++)
        {
            if (!SkipName(message, ref offset)) return -1;
            offset += 4;
            if (offset > message.Length) return -1;
        }
        return offset;
    }

    static Boolean SkipName(Byte[] message, ref Int32 offset)
    {
        var guard = 0;
        while (offset < message.Length && guard++ < 128)
        {
            var len = message[offset];
            if (len == 0)
            {
                offset++;
                return true;
            }
            if ((len & 0xC0) == 0xC0)
            {
                offset += 2;
                return offset <= message.Length;
            }
            if ((len & 0xC0) != 0) return false;
            offset += 1 + len;
        }
        return false;
    }

    static Boolean TryReadName(Byte[] message, ref Int32 offset, out String name) => TryReadName(message, ref offset, out name, 0);

    static Boolean TryReadName(Byte[] message, ref Int32 offset, out String name, Int32 depth)
    {
        name = null;
        if (depth > 8) return false;
        var labels = new List<String>();
        var guard = 0;
        while (offset < message.Length && guard++ < 128)
        {
            var len = message[offset];
            if (len == 0)
            {
                offset++;
                name = String.Join(".", labels);
                return true;
            }
            if ((len & 0xC0) == 0xC0)
            {
                if (offset + 1 >= message.Length) return false;
                var pointer = ((len & 0x3F) << 8) | message[offset + 1];
                offset += 2;
                var cursor = pointer;
                if (!TryReadName(message, ref cursor, out var rest, depth + 1)) return false;
                if (!String.IsNullOrEmpty(rest)) labels.Add(rest);
                name = Join(labels);
                return true;
            }
            if ((len & 0xC0) != 0) return false;
            offset++;
            if (offset + len > message.Length) return false;
            labels.Add(Encoding.ASCII.GetString(message, offset, len));
            offset += len;
        }
        return false;
    }

    /// <summary>压缩指针读出的剩余名字本身可以包含点，不能再按标签拆开。</summary>
    static String Join(List<String> labels)
    {
        if (labels.Count == 0) return "";
        if (labels.Count == 1) return labels[0];
        var last = labels[labels.Count - 1];
        if (last.IndexOf('.') >= 0)
            return String.Join(".", labels.GetRange(0, labels.Count - 1)) + "." + last;
        return String.Join(".", labels);
    }

    static void WriteName(Stream stream, String name)
    {
        if (String.IsNullOrEmpty(name))
        {
            stream.WriteByte(0);
            return;
        }
        foreach (var label in name.Trim('.').Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            if (bytes.Length == 0 || bytes.Length > 63) return;
            stream.WriteByte((Byte)bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }
        stream.WriteByte(0);
    }

    static void Write16(Stream stream, UInt16 value)
    {
        stream.WriteByte((Byte)(value >> 8));
        stream.WriteByte((Byte)value);
    }
}
