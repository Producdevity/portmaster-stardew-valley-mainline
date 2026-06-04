#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

bool SteamAPI_Init(void) { return false; }
void SteamAPI_Shutdown(void) {}
bool SteamAPI_RestartAppIfNecessary(uint32_t app_id) { (void)app_id; return false; }
void SteamAPI_ReleaseCurrentThreadMemory(void) {}
void SteamAPI_WriteMiniDump(uint32_t exception_code, void *exception_info, uint32_t build_id) { (void)exception_code; (void)exception_info; (void)build_id; }
void SteamAPI_SetMiniDumpComment(const char *comment) { (void)comment; }
void SteamAPI_RunCallbacks(void) {}
void SteamAPI_RegisterCallback(void *callback, int callback_id) { (void)callback; (void)callback_id; }
void SteamAPI_UnregisterCallback(void *callback) { (void)callback; }
void SteamAPI_RegisterCallResult(void *callback, uint64_t api_call) { (void)callback; (void)api_call; }
void SteamAPI_UnregisterCallResult(void *callback, uint64_t api_call) { (void)callback; (void)api_call; }
bool SteamAPI_IsSteamRunning(void) { return false; }
int SteamAPI_GetSteamInstallPath(void) { return 0; }
void SteamAPI_SetTryCatchCallbacks(bool enabled) { (void)enabled; }
int SteamAPI_GetHSteamPipe(void) { return 0; }
int SteamAPI_GetHSteamUser(void) { return 0; }
void *SteamInternal_ContextInit(void *context_init) { (void)context_init; return NULL; }
void *SteamInternal_CreateInterface(const char *version) { (void)version; return NULL; }
void *SteamInternal_FindOrCreateUserInterface(int hsteam_user, const char *version) { (void)hsteam_user; (void)version; return NULL; }
void *SteamInternal_FindOrCreateGameServerInterface(int hsteam_user, const char *version) { (void)hsteam_user; (void)version; return NULL; }
void SteamAPI_UseBreakpadCrashHandler(const char *path, const char *version, const char *date, bool full_memory_dumps, void *context, void *callback) {
    (void)path;
    (void)version;
    (void)date;
    (void)full_memory_dumps;
    (void)context;
    (void)callback;
}
void SteamAPI_SetBreakpadAppID(uint32_t app_id) { (void)app_id; }
void SteamAPI_ManualDispatch_Init(void) {}
void SteamAPI_ManualDispatch_RunFrame(int hsteam_pipe) { (void)hsteam_pipe; }
bool SteamAPI_ManualDispatch_GetNextCallback(int hsteam_pipe, void *callback_msg) { (void)hsteam_pipe; (void)callback_msg; return false; }
void SteamAPI_ManualDispatch_FreeLastCallback(int hsteam_pipe) { (void)hsteam_pipe; }
bool SteamAPI_ManualDispatch_GetAPICallResult(int hsteam_pipe, uint64_t steam_api_call, void *callback, int callback_size, int callback_expected, bool *failed) {
    (void)hsteam_pipe;
    (void)steam_api_call;
    (void)callback;
    (void)callback_size;
    (void)callback_expected;
    if (failed != NULL) {
        *failed = true;
    }
    return false;
}
void SteamGameServer_Shutdown(void) {}
void SteamGameServer_RunCallbacks(void) {}
void SteamGameServer_ReleaseCurrentThreadMemory(void) {}
bool SteamGameServer_BSecure(void) { return false; }
uint64_t SteamGameServer_GetSteamID(void) { return 0; }
int SteamGameServer_GetHSteamPipe(void) { return 0; }
int SteamGameServer_GetHSteamUser(void) { return 0; }
bool SteamInternal_GameServer_Init(uint32_t ip, uint16_t game_port, uint16_t query_port, uint16_t steam_port, int server_mode, const char *version) {
    (void)ip;
    (void)game_port;
    (void)query_port;
    (void)steam_port;
    (void)server_mode;
    (void)version;
    return false;
}
void *SteamClient(void) { return NULL; }
void *SteamGameServerClient(void) { return NULL; }
