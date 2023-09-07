using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace QuickMapper
{
    struct PropertyMatchingInfo
    {
        public PropertyInfo SourceProperty { get; set; }
        public PropertyInfo TargetProperty { get; set; }
    }

}
