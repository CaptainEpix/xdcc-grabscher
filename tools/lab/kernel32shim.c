/*
 * The NuGet build of db4o P/Invokes kernel32.dll!FlushFileBuffers, which does not
 * exist on Linux. The XG Docker image avoids it by using Debian's db4o build; for
 * running XG outside Docker (tools/lab) Mono maps kernel32.dll to this library.
 * Mono passes the SafeFileHandle as the file descriptor.
 */
#include <unistd.h>

int FlushFileBuffers(void *handle)
{
	return fsync((int)(long)handle) == 0;
}
