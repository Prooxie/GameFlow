#include <CoreFoundation/CoreFoundation.h>
#include <IOKit/IOReturn.h>
#include <dlfcn.h>
#include <stdio.h>

_Static_assert(sizeof(CFIndex) == 8, "CFIndex must use the 64-bit ABI");
_Static_assert(sizeof(CFOptionFlags) == 8, "CFOptionFlags must use the 64-bit ABI");
_Static_assert(sizeof(CFPropertyListFormat) == 8, "CFPropertyListFormat must use the 64-bit ABI");
_Static_assert(sizeof(IOReturn) == 4, "IOReturn must use the 32-bit ABI");
_Static_assert(sizeof(void *) == 8, "Device handles must use the 64-bit ABI");

int main(void)
{
    /* The SDK omits the virtual-device header. Check the runtime exports without private headers or linker stubs. */
    void *framework = dlopen("/System/Library/Frameworks/IOKit.framework/IOKit", RTLD_NOW | RTLD_LOCAL);
    if (framework == NULL) {
        fprintf(stderr, "Cannot load IOKit: %s\n", dlerror());
        return 1;
    }

    const char *symbols[] = {"IOHIDUserDeviceCreate", "IOHIDUserDeviceHandleReport"};
    for (size_t index = 0; index < sizeof(symbols) / sizeof(symbols[0]); index++) {
        if (dlsym(framework, symbols[index]) == NULL) {
            fprintf(stderr, "IOKit does not export %s\n", symbols[index]);
            dlclose(framework);
            return 1;
        }
    }

    dlclose(framework);
    return 0;
}
