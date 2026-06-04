#include <stddef.h>

void *ImmGetContext(void *window_handle) {
    (void)window_handle;
    return NULL;
}

void *ImmAssociateContext(void *window_handle, void *context_handle) {
    (void)window_handle;
    (void)context_handle;
    return NULL;
}
