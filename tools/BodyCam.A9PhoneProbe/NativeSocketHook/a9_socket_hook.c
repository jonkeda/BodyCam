#define _GNU_SOURCE

#include <arpa/inet.h>
#include <android/log.h>
#include <dlfcn.h>
#include <errno.h>
#include <fcntl.h>
#include <netinet/in.h>
#include <pthread.h>
#include <stdarg.h>
#include <stdbool.h>
#include <stdio.h>
#include <string.h>
#include <sys/socket.h>
#include <sys/syscall.h>
#include <sys/types.h>
#include <time.h>
#include <unistd.h>

static const char *const kLogTag = "A9SockHook";
static const char *const kLogPath = "/data/data/com.bodycam.a9phoneprobe/files/a9-socket-hook.log";
static const size_t kMaxHexBytes = 192;

static pthread_once_t g_once = PTHREAD_ONCE_INIT;
static pthread_mutex_t g_log_lock = PTHREAD_MUTEX_INITIALIZER;

static ssize_t (*real_sendto)(int, const void *, size_t, int, const struct sockaddr *, socklen_t);
static ssize_t (*real_recvfrom)(int, void *, size_t, int, struct sockaddr *, socklen_t *);
static ssize_t (*real_send)(int, const void *, size_t, int);
static ssize_t (*real_recv)(int, void *, size_t, int);
static int (*real_connect)(int, const struct sockaddr *, socklen_t);

static void init_real(void)
{
    real_sendto = (ssize_t (*)(int, const void *, size_t, int, const struct sockaddr *, socklen_t))dlsym(RTLD_NEXT, "sendto");
    real_recvfrom = (ssize_t (*)(int, void *, size_t, int, struct sockaddr *, socklen_t *))dlsym(RTLD_NEXT, "recvfrom");
    real_send = (ssize_t (*)(int, const void *, size_t, int))dlsym(RTLD_NEXT, "send");
    real_recv = (ssize_t (*)(int, void *, size_t, int))dlsym(RTLD_NEXT, "recv");
    real_connect = (int (*)(int, const struct sockaddr *, socklen_t))dlsym(RTLD_NEXT, "connect");
}

static long now_ms(void)
{
    struct timespec ts;
    clock_gettime(CLOCK_REALTIME, &ts);
    return (long)(ts.tv_sec * 1000L + ts.tv_nsec / 1000000L);
}

static long tid(void)
{
    return (long)syscall(SYS_gettid);
}

static void format_sockaddr(const struct sockaddr *addr, socklen_t len, char *out, size_t out_len)
{
    if (out_len == 0)
        return;

    out[0] = '\0';
    if (addr == NULL || len == 0)
    {
        snprintf(out, out_len, "-");
        return;
    }

    if (addr->sa_family == AF_INET && len >= (socklen_t)sizeof(struct sockaddr_in))
    {
        const struct sockaddr_in *in4 = (const struct sockaddr_in *)addr;
        char ip[INET_ADDRSTRLEN] = {0};
        inet_ntop(AF_INET, &in4->sin_addr, ip, sizeof(ip));
        snprintf(out, out_len, "%s:%u", ip, (unsigned)ntohs(in4->sin_port));
        return;
    }

    if (addr->sa_family == AF_INET6 && len >= (socklen_t)sizeof(struct sockaddr_in6))
    {
        const struct sockaddr_in6 *in6 = (const struct sockaddr_in6 *)addr;
        char ip[INET6_ADDRSTRLEN] = {0};
        inet_ntop(AF_INET6, &in6->sin6_addr, ip, sizeof(ip));
        snprintf(out, out_len, "[%s]:%u", ip, (unsigned)ntohs(in6->sin6_port));
        return;
    }

    snprintf(out, out_len, "family=%d len=%u", (int)addr->sa_family, (unsigned)len);
}

static void format_fd_name(int fd, char *out, size_t out_len)
{
    if (out_len == 0)
        return;

    char local[96];
    char peer[96];
    struct sockaddr_storage local_addr;
    struct sockaddr_storage peer_addr;
    socklen_t local_len = sizeof(local_addr);
    socklen_t peer_len = sizeof(peer_addr);

    if (getsockname(fd, (struct sockaddr *)&local_addr, &local_len) == 0)
        format_sockaddr((const struct sockaddr *)&local_addr, local_len, local, sizeof(local));
    else
        snprintf(local, sizeof(local), "-");

    if (getpeername(fd, (struct sockaddr *)&peer_addr, &peer_len) == 0)
        format_sockaddr((const struct sockaddr *)&peer_addr, peer_len, peer, sizeof(peer));
    else
        snprintf(peer, sizeof(peer), "-");

    snprintf(out, out_len, "local=%s peer=%s", local, peer);
}

static void format_hex(const void *buf, size_t len, char *out, size_t out_len)
{
    static const char hex[] = "0123456789ABCDEF";
    const unsigned char *bytes = (const unsigned char *)buf;
    size_t max = len < kMaxHexBytes ? len : kMaxHexBytes;
    size_t pos = 0;

    if (out_len == 0)
        return;

    for (size_t i = 0; i < max && pos + 2 < out_len; i++)
    {
        out[pos++] = hex[(bytes[i] >> 4) & 0xF];
        out[pos++] = hex[bytes[i] & 0xF];
    }

    if (len > max && pos + 3 < out_len)
    {
        out[pos++] = '.';
        out[pos++] = '.';
        out[pos++] = '.';
    }

    out[pos] = '\0';
}

static void append_log(const char *fmt, ...)
{
    char line[2048];
    va_list args;

    va_start(args, fmt);
    int count = vsnprintf(line, sizeof(line), fmt, args);
    va_end(args);

    if (count < 0)
        return;

    size_t length = (size_t)count;
    if (length >= sizeof(line))
        length = sizeof(line) - 1;
    line[length++] = '\n';

    pthread_mutex_lock(&g_log_lock);
    int fd = open(kLogPath, O_CREAT | O_WRONLY | O_APPEND | O_CLOEXEC, 0600);
    if (fd >= 0)
    {
        (void)write(fd, line, length);
        close(fd);
    }
    pthread_mutex_unlock(&g_log_lock);
}

__attribute__((constructor)) static void socket_hook_loaded(void)
{
    pthread_once(&g_once, init_real);
    append_log("loaded ts=%ld pid=%ld tid=%ld", now_ms(), (long)getpid(), tid());
    __android_log_print(ANDROID_LOG_INFO, kLogTag, "loaded");
}

int connect(int fd, const struct sockaddr *addr, socklen_t addrlen)
{
    pthread_once(&g_once, init_real);
    char target[128];
    char names[224];
    int before_errno = errno;
    format_sockaddr(addr, addrlen, target, sizeof(target));
    format_fd_name(fd, names, sizeof(names));
    int ret = real_connect(fd, addr, addrlen);
    int saved_errno = errno;
    append_log("connect ts=%ld tid=%ld fd=%d %s target=%s ret=%d errno=%d", now_ms(), tid(), fd, names, target, ret, saved_errno);
    errno = ret == 0 ? before_errno : saved_errno;
    return ret;
}

ssize_t sendto(int fd, const void *buf, size_t len, int flags, const struct sockaddr *dest_addr, socklen_t addrlen)
{
    pthread_once(&g_once, init_real);
    char target[128];
    char names[224];
    char hex[kMaxHexBytes * 2 + 8];
    int before_errno = errno;
    format_sockaddr(dest_addr, addrlen, target, sizeof(target));
    format_fd_name(fd, names, sizeof(names));
    format_hex(buf, len, hex, sizeof(hex));
    ssize_t ret = real_sendto(fd, buf, len, flags, dest_addr, addrlen);
    int saved_errno = errno;
    append_log("sendto ts=%ld tid=%ld fd=%d %s target=%s len=%zu ret=%zd errno=%d hex=%s", now_ms(), tid(), fd, names, target, len, ret, saved_errno, hex);
    errno = ret >= 0 ? before_errno : saved_errno;
    return ret;
}

ssize_t recvfrom(int fd, void *buf, size_t len, int flags, struct sockaddr *src_addr, socklen_t *addrlen)
{
    pthread_once(&g_once, init_real);
    int before_errno = errno;
    ssize_t ret = real_recvfrom(fd, buf, len, flags, src_addr, addrlen);
    int saved_errno = errno;
    if (ret >= 0)
    {
        char source[128];
        char names[224];
        char hex[kMaxHexBytes * 2 + 8];
        format_sockaddr(src_addr, addrlen == NULL ? 0 : *addrlen, source, sizeof(source));
        format_fd_name(fd, names, sizeof(names));
        format_hex(buf, (size_t)ret, hex, sizeof(hex));
        append_log("recvfrom ts=%ld tid=%ld fd=%d %s source=%s want=%zu ret=%zd errno=%d hex=%s", now_ms(), tid(), fd, names, source, len, ret, saved_errno, hex);
    }
    errno = ret >= 0 ? before_errno : saved_errno;
    return ret;
}

ssize_t send(int fd, const void *buf, size_t len, int flags)
{
    pthread_once(&g_once, init_real);
    char names[224];
    char hex[kMaxHexBytes * 2 + 8];
    int before_errno = errno;
    format_fd_name(fd, names, sizeof(names));
    format_hex(buf, len, hex, sizeof(hex));
    ssize_t ret = real_send(fd, buf, len, flags);
    int saved_errno = errno;
    append_log("send ts=%ld tid=%ld fd=%d %s len=%zu ret=%zd errno=%d hex=%s", now_ms(), tid(), fd, names, len, ret, saved_errno, hex);
    errno = ret >= 0 ? before_errno : saved_errno;
    return ret;
}

ssize_t recv(int fd, void *buf, size_t len, int flags)
{
    pthread_once(&g_once, init_real);
    int before_errno = errno;
    ssize_t ret = real_recv(fd, buf, len, flags);
    int saved_errno = errno;
    if (ret >= 0)
    {
        char names[224];
        char hex[kMaxHexBytes * 2 + 8];
        format_fd_name(fd, names, sizeof(names));
        format_hex(buf, (size_t)ret, hex, sizeof(hex));
        append_log("recv ts=%ld tid=%ld fd=%d %s want=%zu ret=%zd errno=%d hex=%s", now_ms(), tid(), fd, names, len, ret, saved_errno, hex);
    }
    errno = ret >= 0 ? before_errno : saved_errno;
    return ret;
}
