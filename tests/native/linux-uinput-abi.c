#include <linux/input.h>
#include <linux/uinput.h>
#include <stddef.h>

_Static_assert(sizeof(struct input_event) == 24, "input_event must use the 64-bit ABI");
_Static_assert(sizeof(struct uinput_setup) == 92, "uinput_setup size changed");
_Static_assert(sizeof(struct uinput_abs_setup) == 28, "uinput_abs_setup size changed");
_Static_assert(offsetof(struct uinput_abs_setup, absinfo) == 4, "absinfo alignment changed");
_Static_assert(UI_SET_ABSBIT == 0x40045567, "UI_SET_ABSBIT changed");
_Static_assert(UI_ABS_SETUP == 0x401C5504, "UI_ABS_SETUP changed");
_Static_assert(UI_SET_PHYS == 0x4008556C, "UI_SET_PHYS changed");
_Static_assert(EVIOCGABS(ABS_X) == 0x80184540, "EVIOCGABS changed");
_Static_assert(EVIOCGKEY(96) == 0x80604518, "EVIOCGKEY changed");

int main(void)
{
    return 0;
}
