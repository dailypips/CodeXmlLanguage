using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ICSharpCode.ILSpy
{
    public class StringWriterWithEncoding : StringWriter
    {

        private readonly Encoding encoding;

        public StringWriterWithEncoding() : base() { }

        public StringWriterWithEncoding(IFormatProvider formatProvider) : base(formatProvider) { }

        public StringWriterWithEncoding(StringBuilder sb) : base(sb) { }

        public StringWriterWithEncoding(StringBuilder sb, IFormatProvider formatProvider) : base(sb, formatProvider) { }

        public StringWriterWithEncoding(Encoding newEncoding) : base() { encoding = newEncoding; }

        public StringWriterWithEncoding(IFormatProvider formatProvider, Encoding newEncoding) : base(formatProvider) { encoding = newEncoding; }

        public StringWriterWithEncoding(StringBuilder sb, IFormatProvider formatProvider, Encoding newEncoding) : base(sb, formatProvider) { encoding = newEncoding; }

        public StringWriterWithEncoding(StringBuilder sb, Encoding newEncoding) : base(sb) { encoding = newEncoding; }

        public override Encoding Encoding { get { return encoding ?? base.Encoding; } }

    }
}
