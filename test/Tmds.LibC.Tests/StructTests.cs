using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;

namespace Tmds.LibC.Tests
{
    public class StructTests
    {
        // no need to prepend these with the 'struct' keyword
        private static string[] s_typedefs = new [] {
            "epoll_data_t",
            "size_t",
            "ssize_t",
            "sa_family_t",
            "pid_t",
            "uid_t",
            "gid_t",
            "socklen_t",
            "off_t",
            "time_t",
            "mode_t",
            "syscall_arg",
            "long_t",
            "cpu_set_t",
        };

        // fields with some weirdness
        private static string[] s_skipSizeCheckForFields = new[] {
            // linux uses size_t instead of int
            "msghdr.msg_controllen",
            "msghdr.msg_iovlen",
            "cmsghdr.cmsg_len"
        };

        [Theory]
        [MemberData(nameof(Data))]
        public void Structs(CIncludes includes, Type type)
        {
            string name = type.Name;
            bool? supported = TestEnvironment.Current.SupportsStruct(name);
            if (supported == false)
            {
                return;
            }

            string structName = s_typedefs.Contains(name) ? name : $"struct {name}";

            using (var program = new CProgram())
            {
                if (TestEnvironment.Current.SupportsHeaders(includes.Headers) == false)
                {
                    return;
                }

                program.Define("_GNU_SOURCE");
                program.Include(includes.Headers);
                program.Include("stddef.h");
                program.AppendLine("#define member_size(type, member) sizeof(((type *)0)->member)");

                // define syscall_arg and long_t
                program.AppendLine("typedef long syscall_arg;");
                program.AppendLine("typedef long long_t;");

                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
                {
                    string fieldName = field.Name;
                    int fieldOffset = Marshal.OffsetOf(type, fieldName).ToInt32();
                    bool checkField = fieldName.StartsWith("<") || // Backing field
                                    (fieldName.StartsWith("_") && !fieldName.StartsWith("__")); // Starts with single underscpre
                    if (!checkField)
                    {
                        continue;
                    }
                    if (fieldName.StartsWith("<"))
                    {
                        fieldName = fieldName.Substring(1, fieldName.IndexOf('>') - 1);
                    }
                    else if (fieldName.StartsWith("_"))
                    {
                        fieldName = fieldName.Substring(1);
                    }

                    program.StaticAssert($"offsetof({structName}, {fieldName}) == {fieldOffset}", $"{name}.{fieldName} is not at expected offset");

                    Type fieldType = field.FieldType;
                    int fieldLength = 1;
                    var attr = field.GetCustomAttribute(typeof(FixedBufferAttribute), false) as FixedBufferAttribute;
                    if (attr != null)
                    {
                        fieldType = attr.ElementType;
                        fieldLength = attr.Length;
                    }

                    if (s_skipSizeCheckForFields.Contains($"{name}.{fieldName}"))
                    {
                        continue;
                    }

                    program.StaticAssert($"member_size({structName}, {fieldName}) == {Marshal.SizeOf(fieldType) * fieldLength}", $"{name}.{fieldName} is not of expected size");
                }

                program.StaticAssert($"sizeof({structName}) == {Marshal.SizeOf(type)}", $"{structName} does not have expected size");

                program.Compile();
            }
        }

        [Theory]
        [MemberData(nameof(Data))]
        public void SizeOf(CIncludes includes, Type type)
        {
            int actualSize = Marshal.SizeOf(type);

            PropertyInfo property = typeof(SizeOf).GetProperty(type.Name);
            Assert.NotNull(property);
            int declaredSize = (ushort)property.GetGetMethod().Invoke(null, null);

            Assert.Equal(actualSize, declaredSize);
        }

        public static string[] s_uncheckedTypes = new string[] {
            "io_event",
            "iocb",
            "aio_ring",
            "aio_context_t",
        };

        [Fact]
        public void UncheckedStructs() // Verifies s_uncheckedTypes is up-to-date
        {
            IEnumerable<Type> types = typeof(Definitions).Assembly.GetTypes();

            // Filter out public structs
            types = types.Where(t => t.IsValueType && t.IsPublic);

            // Remove unchecked types
            types = types.Where(t => !s_uncheckedTypes.Contains(t.Name));

            // Remove checked functions
            types = types.Where(t => !Data.Any(d => ((Type)((object[])d)[1]) == t));

            Assert.True(!types.Any(),
                $"Unchecked types: {string.Join(", ", types.Select(t => t.Name).Distinct())}");
        }

        [Fact]
        public void SizeOfNames()
        {
            var properties = typeof(SizeOf).GetProperties();

            foreach (var property in properties)
            {
                var type = typeof(Definitions).Assembly.GetTypes().SingleOrDefault(t => t.Name == property.Name);
                Assert.True(type != null, $"SizeOf.{property.Name} has no matching struct");
            }
        }

        public static TheoryData<CIncludes, Type> Data =>
            new TheoryData<CIncludes, Type>
                {
                    { "sys/uio.h", typeof(iovec) },
                    { "sys/socket.h", typeof(ucred) },
                    { "sys/socket.h", typeof(mmsghdr) },
                    { "sys/socket.h", typeof(linger) },
                    { "sys/socket.h", typeof(sockaddr) },
                    { "sys/socket.h", typeof(sockaddr_storage) },
                    { "sys/socket.h", typeof(cmsghdr) },
                    { "sys/socket.h", typeof(msghdr) },
                    { "netinet/in.h", typeof(in_addr) },
                    { "netinet/in.h", typeof(in6_addr) },
                    { "netinet/in.h", typeof(sockaddr_in) },
                    { "netinet/in.h", typeof(sockaddr_in6) },
                    { "netinet/in.h", typeof(ipv6_mreq) },
                    { "netinet/in.h", typeof(ip_opts) },
                    { "netinet/in.h", typeof(ip_mreq) },
                    { "netinet/in.h", typeof(ip_mreqn) },
                    { "netinet/in.h", typeof(ip_mreq_source) },
                    { "netinet/in.h", typeof(group_req) },
                    { "netinet/in.h", typeof(group_source_req) },
                    { "netinet/in.h", typeof(group_filter) },
                    { "netinet/in.h", typeof(in_pktinfo) },
                    { "netinet/in.h", typeof(in6_pktinfo) },
                    { "netinet/in.h", typeof(ip6_mtuinfo) },
                    { "fcntl.h", typeof(flock) },
                    { "fcntl.h", typeof(file_handle) },
                    { "fcntl.h", typeof(f_owner_ex) },
                    { "sys/un.h", typeof(sockaddr_un) },
                    { "sys/epoll.h", typeof(epoll_data_t) },
                    { "sys/epoll.h", typeof(epoll_event) },
                    { new CIncludes(new string[] { "linux/time.h" /* timespec */, "linux/errqueue.h"}), typeof(sock_extended_err) },
                    { new CIncludes(new string[] { "linux/time.h" /* timespec */, "linux/errqueue.h" }), typeof(scm_timestamping) },
                    { "sys/types.h", typeof(size_t) },
                    { "sys/types.h", typeof(ssize_t) },
                    { "sys/socket.h", typeof(sa_family_t) },
                    { "sys/types.h", typeof(pid_t) },
                    { "sys/types.h", typeof(uid_t) },
                    { "sys/types.h", typeof(gid_t) },
                    { "sys/socket.h", typeof(socklen_t) },
                    { "sys/types.h", typeof(off_t) },
                    { "sys/time.h", typeof(time_t) },
                    { "sys/time.h", typeof(timespec) },
                    { "sys/types.h", typeof(mode_t) },
                    { "sys/types.h", typeof(syscall_arg) },
                    { "sys/types.h", typeof(long_t) },
                    { "sys/ioctl.h", typeof(winsize) },
                    { "sched.h", typeof(cpu_set_t) },
                };
    }
}
