#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

bool SteamEncryptedAppTicket_BDecryptTicket(uint8_t *encrypted_ticket, uint32_t encrypted_ticket_size, uint8_t *decrypted_ticket, uint32_t *decrypted_ticket_size, uint8_t *key, int key_size) {
    (void)encrypted_ticket;
    (void)encrypted_ticket_size;
    (void)decrypted_ticket;
    (void)decrypted_ticket_size;
    (void)key;
    (void)key_size;
    return false;
}

bool SteamEncryptedAppTicket_BIsTicketForApp(uint8_t *ticket, uint32_t ticket_size, uint32_t app_id) {
    (void)ticket;
    (void)ticket_size;
    (void)app_id;
    return false;
}

uint32_t SteamEncryptedAppTicket_GetTicketIssueTime(uint8_t *ticket, uint32_t ticket_size) {
    (void)ticket;
    (void)ticket_size;
    return 0;
}

void SteamEncryptedAppTicket_GetTicketSteamID(uint8_t *ticket, uint32_t ticket_size, void *steam_id) {
    (void)ticket;
    (void)ticket_size;
    (void)steam_id;
}

uint32_t SteamEncryptedAppTicket_GetTicketAppID(uint8_t *ticket, uint32_t ticket_size) {
    (void)ticket;
    (void)ticket_size;
    return 0;
}

bool SteamEncryptedAppTicket_BUserOwnsAppInTicket(uint8_t *ticket, uint32_t ticket_size, uint32_t app_id) {
    (void)ticket;
    (void)ticket_size;
    (void)app_id;
    return false;
}

bool SteamEncryptedAppTicket_BUserIsVacBanned(uint8_t *ticket, uint32_t ticket_size) {
    (void)ticket;
    (void)ticket_size;
    return false;
}

void *SteamEncryptedAppTicket_GetUserVariableData(uint8_t *ticket, uint32_t ticket_size, uint32_t *data_size) {
    (void)ticket;
    (void)ticket_size;
    if (data_size != NULL) {
        *data_size = 0;
    }
    return NULL;
}

bool SteamEncryptedAppTicket_BIsTicketSigned(uint8_t *ticket, uint32_t ticket_size, uint8_t *public_key, uint32_t public_key_size) {
    (void)ticket;
    (void)ticket_size;
    (void)public_key;
    (void)public_key_size;
    return false;
}

bool SteamEncryptedAppTicket_BIsLicenseBorrowed(uint8_t *ticket, uint32_t ticket_size) {
    (void)ticket;
    (void)ticket_size;
    return false;
}

bool SteamEncryptedAppTicket_BIsLicenseTemporary(uint8_t *ticket, uint32_t ticket_size) {
    (void)ticket;
    (void)ticket_size;
    return false;
}
