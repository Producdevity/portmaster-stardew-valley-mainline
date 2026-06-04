#include <stddef.h>
#include <stdint.h>

int SetWindowLong(void *window_handle, int index, int value) {
    (void)window_handle;
    (void)index;
    (void)value;
    return 0;
}

void *CallWindowProc(void *window_proc, void *window_handle, unsigned int message, void *wparam, void *lparam) {
    (void)window_proc;
    (void)window_handle;
    (void)message;
    (void)wparam;
    (void)lparam;
    return NULL;
}
