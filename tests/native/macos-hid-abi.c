#include <CoreFoundation/CoreFoundation.h>
#include <IOKit/hid/IOHIDUserDevice.h>

_Static_assert(sizeof(CFIndex) == 8, "CFIndex must use the 64-bit ABI");

int main(int argc, char **argv)
{
    (void)argv;
    if (argc > 1) {
        uint8_t report[17] = {0};
        IOHIDUserDeviceRef device = IOHIDUserDeviceCreate(kCFAllocatorDefault, NULL);
        if (device != NULL) {
            IOReturn result = IOHIDUserDeviceHandleReport(device, report, sizeof(report));
            CFRelease(device);
            return result != kIOReturnSuccess;
        }
    }
    return 0;
}
