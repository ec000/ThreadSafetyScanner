/* strtok(fake, ","); should be ignored */
const char *comment_only = "inet_ntoa(fake) must be ignored";
#include <string.h>
#include <time.h>
#include <stdlib.h>
void example(char *input) {
    char *a = strtok(input, ",");
    struct tm *tm_ptr = localtime((time_t *)0);
    (void)a;
    (void)tm_ptr;
    /* another fake: gethostbyname("example.com") */
    int b = rand();
    (void)b;
}
