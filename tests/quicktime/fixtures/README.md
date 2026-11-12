# QuickTime screen capture messages

Messages recorded from a real iPhone by quicktime_video_hack, copied
unchanged from its `screencapture/packet/fixtures` folder:
https://github.com/danielpaulus/quicktime_video_hack

Fetched on 2026-11-12, at commit `d81396e2e7758d98c2a594853b64f98b54a8a871`
(the head of `master`, dated 2023-05-04).

Every file holds one message as it travels on USB, starting with its
4-byte little-endian length, except `asyn-sprp`, which was recorded without
it: the test adds the length back before parsing it.

The `*-request` files are what the device sends, the `*-reply` files what
the host answers. `asyn-hpd1`, `asyn-hpd0` and `asyn-need` are host
messages; `asyn-feed`, `asyn-rels` and `asyn-sprp` come from the device.

## License of quicktime_video_hack

MIT License

Copyright (c) 2019 danielpaulus

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
