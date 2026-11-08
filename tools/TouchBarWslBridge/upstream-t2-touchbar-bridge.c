// SPDX-License-Identifier: GPL-2.0-only
// Userspace Apple T2 Touch Bar display/touch bridge for Linux/WSL.
// The wire protocol was implemented from the GPL-2.0-only Linux appletbdrm driver.
// Protocol credit: Kerem Karabay and Linux appletbdrm contributors.

#include <libusb-1.0/libusb.h>
#include <pthread.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#define APPLE_VID 0x05ac
#define TOUCHBAR_PID 0x8302
#define DISPLAY_CONFIGURATION 2
#define DISPLAY_INTERFACE 1
#define DISPLAY_OUT 0x01
#define DISPLAY_IN 0x82
#define TOUCH_IN 0x81
#define USB_TIMEOUT_MS 5000

static void write_u16(uint8_t *p, uint16_t value) { p[0] = value; p[1] = value >> 8; }
static void write_u32(uint8_t *p, uint32_t value) { p[0] = value; p[1] = value >> 8; p[2] = value >> 16; p[3] = value >> 24; }
static void write_u64(uint8_t *p, uint64_t value) { write_u32(p, value); write_u32(p + 4, value >> 32); }
static uint32_t read_u32(const uint8_t *p) { return p[0] | ((uint32_t)p[1] << 8) | ((uint32_t)p[2] << 16) | ((uint32_t)p[3] << 24); }
static uint64_t read_u64(const uint8_t *p) { return read_u32(p) | ((uint64_t)read_u32(p + 4) << 32); }

struct touch_state { libusb_device_handle *device; uint16_t width; uint16_t height; volatile int running; };

static int read_exact(FILE *stream, uint8_t *buffer, size_t length) {
    size_t offset = 0;
    while (offset < length) {
        size_t count = fread(buffer + offset, 1, length - offset, stream);
        if (!count) return feof(stream) ? 0 : -1;
        offset += count;
    }
    return 1;
}

static int usb_write(libusb_device_handle *device, uint8_t *data, int length) {
    int sent = 0;
    int result = libusb_bulk_transfer(device, DISPLAY_OUT, data, length, &sent, USB_TIMEOUT_MS);
    if (result || sent != length) {
        fprintf(stderr, "USB write failed: %s (%d/%d)\n", libusb_error_name(result), sent, length);
        return result ? result : LIBUSB_ERROR_IO;
    }
    return 0;
}

static int usb_read(libusb_device_handle *device, uint8_t *data, int capacity, int *received) {
    *received = 0;
    return libusb_bulk_transfer(device, DISPLAY_IN, data, capacity, received, USB_TIMEOUT_MS);
}

static int request(libusb_device_handle *device, uint32_t key) {
    uint8_t packet[32] = {0};
    write_u32(packet, 0x15120002);
    write_u32(packet + 12, 16);
    write_u32(packet + 16, key);
    write_u32(packet + 28, 16);
    return usb_write(device, packet, sizeof packet);
}

static int open_touchbar(libusb_device_handle **out, uint16_t *width, uint16_t *height, int *touch_claimed) {
    libusb_device_handle *device = libusb_open_device_with_vid_pid(NULL, APPLE_VID, TOUCHBAR_PID);
    if (!device) { fprintf(stderr, "Touch Bar 05ac:8302 is not attached to WSL.\n"); return LIBUSB_ERROR_NO_DEVICE; }
    libusb_set_auto_detach_kernel_driver(device, 1);

    int configuration = 0;
    int result = libusb_get_configuration(device, &configuration);
    if (result) goto fail;
    if (configuration != DISPLAY_CONFIGURATION) {
        if (libusb_kernel_driver_active(device, 0) == 1) libusb_detach_kernel_driver(device, 0);
        result = libusb_set_configuration(device, 0);
        if (result) goto fail;
        sleep(1);
        result = libusb_set_configuration(device, DISPLAY_CONFIGURATION);
        if (result) goto fail;
        libusb_close(device);
        sleep(3);
        device = libusb_open_device_with_vid_pid(NULL, APPLE_VID, TOUCHBAR_PID);
        if (!device) return LIBUSB_ERROR_NO_DEVICE;
        libusb_set_auto_detach_kernel_driver(device, 1);
    }

    *touch_claimed = libusb_claim_interface(device, 0) == 0;
    fprintf(stderr, *touch_claimed ? "Touch input interface ready.\n" : "Touch input interface unavailable.\n");
    result = libusb_claim_interface(device, DISPLAY_INTERFACE);
    if (result) goto fail;
    sleep(2);

    result = request(device, 0x47494e46); /* GINF */
    if (result) goto release;
    uint8_t response[256] = {0};
    int received = 0;
    result = usb_read(device, response, sizeof response, &received);
    if (result || received < 65 || read_u32(response) != 0x01140000 || read_u32(response + 16) != 0x47494e46) {
        result = result ? result : LIBUSB_ERROR_IO;
        goto release;
    }
    uint32_t detected_width = read_u32(response + 32);
    uint32_t detected_height = read_u32(response + 36);
    if (!detected_width || !detected_height || detected_width > UINT16_MAX || detected_height > UINT16_MAX) { result = LIBUSB_ERROR_OVERFLOW; goto release; }
    result = request(device, 0x52454459); /* REDY */
    if (result) goto release;
    result = request(device, 0x434c4452); /* CLDR */
    if (result) goto release;
    *width = detected_width;
    *height = detected_height;
    *out = device;
    fprintf(stderr, "Touch Bar bridge ready: %ux%u.\n", *width, *height);
    return 0;

release:
    libusb_release_interface(device, DISPLAY_INTERFACE);
    if (*touch_claimed) libusb_release_interface(device, 0);
fail:
    libusb_close(device);
    return result;
}

static void *read_touches(void *opaque) {
    struct touch_state *state = opaque;
    int was_down = 0;
    uint8_t previous_id = 0;
    uint16_t previous_x = 0, previous_y = 0;
    while (state->running) {
        uint8_t report[64] = {0};
        int received = 0;
        int result = libusb_interrupt_transfer(state->device, TOUCH_IN, report, sizeof report, &received, 100);
        if (result == LIBUSB_ERROR_TIMEOUT) continue;
        if (result) { if (state->running) fprintf(stderr, "Touch read failed: %s.\n", libusb_error_name(result)); break; }
        if (received < 44) continue;

        int is_down = 0;
        uint8_t id = 0;
        uint16_t x = previous_x, y = previous_y;
        for (int slot = 0; slot < 11; ++slot) {
            const uint8_t *contact = report + slot * 4;
            if (!(contact[0] & 0x10)) continue;
            is_down = 1;
            id = contact[0] & 0x0f;
            uint16_t raw_x = contact[1] | ((uint16_t)contact[2] << 8);
            x = (uint32_t)raw_x * state->width / 32767u;
            y = (uint32_t)contact[3] * state->height / 127u;
            if (x >= state->width) x = state->width - 1;
            if (y >= state->height) y = state->height - 1;
            break;
        }
        if (!was_down && is_down) fprintf(stderr, "TOUCH %u %u DOWN\n", x, y);
        else if (was_down && !is_down) fprintf(stderr, "TOUCH %u %u UP\n", previous_x, previous_y);
        else if (was_down && is_down && (id != previous_id || x != previous_x || y != previous_y)) fprintf(stderr, "TOUCH %u %u MOVE\n", x, y);
        was_down = is_down; previous_id = id; previous_x = x; previous_y = y;
    }
    return NULL;
}

static int send_frame(libusb_device_handle *device, const uint8_t *rgba, uint16_t width, uint16_t height, uint64_t timestamp) {
    size_t rgb_length = (size_t)width * height * 3;
    size_t footer = 60 + rgb_length;
    size_t total = (footer + 80 + 15) & ~(size_t)15;
    uint8_t *packet = calloc(1, total);
    if (!packet) return LIBUSB_ERROR_NO_MEM;
    write_u32(packet, 0x00120002); write_u32(packet + 4, 9); write_u32(packet + 12, total - 16);
    write_u16(packet + 16, 1); packet[18] = timestamp; write_u16(packet + 52, width); write_u16(packet + 54, height); write_u32(packet + 56, rgb_length);
    uint8_t *rgb = packet + 60;
    for (uint16_t x = 0; x < width; ++x) for (uint16_t y = 0; y < height; ++y) {
        size_t source = ((size_t)(height - 1 - y) * width + x) * 4;
        size_t target = ((size_t)x * height + y) * 3;
        rgb[target] = rgba[source]; rgb[target + 1] = rgba[source + 1]; rgb[target + 2] = rgba[source + 2];
    }
    write_u32(packet + footer + 12, 0xfffe); write_u32(packet + footer + 28, 0x80001); write_u64(packet + footer + 32, timestamp); write_u32(packet + footer + 52, 0x80002); write_u32(packet + footer + 76, 0xffff);
    int result = usb_write(device, packet, total);
    free(packet);
    if (result) return result;
    uint8_t acknowledgement[256];
    for (int attempt = 0; attempt < 4; ++attempt) {
        int received = 0;
        result = usb_read(device, acknowledgement, sizeof acknowledgement, &received);
        if (result) return result;
        if (received >= 40 && read_u32(acknowledgement + 16) == 0x5544434c && read_u64(acknowledgement + 32) == timestamp) return 0;
    }
    return LIBUSB_ERROR_IO;
}

int main(void) {
    setvbuf(stdin, NULL, _IONBF, 0); setvbuf(stdout, NULL, _IONBF, 0); setvbuf(stderr, NULL, _IONBF, 0);
    int result = libusb_init(NULL);
    if (result) return 10;
    libusb_device_handle *device = NULL;
    uint16_t width = 0, height = 0;
    int touch_claimed = 0;
    result = open_touchbar(&device, &width, &height, &touch_claimed);
    if (result) { libusb_exit(NULL); return 11; }
    /* Keep startup control traffic line-oriented. WSL can delay the first
       binary stdout write after a Windows lock/unlock or USB reset even when
       later frame acknowledgements work normally. */
    fprintf(stderr, "READY %u %u\n", width, height);

    struct touch_state state = {.device = device, .width = width, .height = height, .running = touch_claimed};
    pthread_t touch_thread;
    int touch_thread_started = touch_claimed && pthread_create(&touch_thread, NULL, read_touches, &state) == 0;
    size_t frame_length = (size_t)width * height * 4;
    uint8_t *rgba = malloc(frame_length);
    uint64_t timestamp = 1;
    if (!rgba) result = LIBUSB_ERROR_NO_MEM;
    while (rgba) {
        uint8_t header[8];
        int status = read_exact(stdin, header, sizeof header);
        if (status <= 0 || !memcmp(header, "QUIT", 4)) break;
        uint32_t length = read_u32(header + 4);
        if (memcmp(header, "FRM1", 4) || length != frame_length || read_exact(stdin, rgba, frame_length) != 1) { result = LIBUSB_ERROR_INVALID_PARAM; break; }
        result = send_frame(device, rgba, width, height, timestamp);
        if (result) break;
        uint8_t acknowledgement[12] = {'A','C','K','1'};
        write_u64(acknowledgement + 4, timestamp++); fwrite(acknowledgement, 1, sizeof acknowledgement, stdout);
    }
    free(rgba);
    state.running = 0;
    if (touch_thread_started) pthread_join(touch_thread, NULL);
    libusb_release_interface(device, DISPLAY_INTERFACE);
    if (touch_claimed) libusb_release_interface(device, 0);
    libusb_close(device); libusb_exit(NULL);
    return result ? 12 : 0;
}
