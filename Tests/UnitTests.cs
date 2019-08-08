using System;
using Xunit;
using SQLitePersistentKeyValueStore;
using System.IO;
using System.Threading;

namespace Tests
{
    public class UnitTests
    {
        [Fact]
        public void put_and_get_across_different_sessions()
        {
            var tempFile = Path.GetTempFileName();

            var key = "a";
            var value = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

            using (var myStore = new Store(tempFile))
            {
                myStore.Put(key, value);
            }

            using (var myStore = new Store(tempFile))
            {
                var actualStoredValue = myStore.Get(key);
                Assert.Equal(value, actualStoredValue);
            }

            File.Delete(tempFile);

        }
    }
}
