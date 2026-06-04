#define _GNU_SOURCE

#include <dlfcn.h>
#include <stddef.h>

typedef char *(*SDL_GetClipboardText_Fn)(void);
typedef int (*SDL_SetClipboardText_Fn)(const char *);

static void *load_sdl(void) {
    static void *handle = NULL;

    if (handle == NULL) {
        handle = dlopen("libSDL2-2.0.so.0", RTLD_LAZY | RTLD_GLOBAL);
    }

    return handle;
}

char *SDL_GetClipboardText(void) {
    void *handle = load_sdl();
    if (handle == NULL) {
        return NULL;
    }

    SDL_GetClipboardText_Fn fn = (SDL_GetClipboardText_Fn)dlsym(handle, "SDL_GetClipboardText");
    if (fn == NULL) {
        return NULL;
    }

    return fn();
}

int SDL_SetClipboardText(const char *text) {
    void *handle = load_sdl();
    if (handle == NULL) {
        return -1;
    }

    SDL_SetClipboardText_Fn fn = (SDL_SetClipboardText_Fn)dlsym(handle, "SDL_SetClipboardText");
    if (fn == NULL) {
        return -1;
    }

    return fn(text);
}
