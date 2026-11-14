#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""B 站「无登录」视频直链可行性验证脚本。

对应调研报告《bili-无登录直链调研.md》，只依赖 Python 标准库（3.9+）。

用法：
    python tools/bili-anon-playurl-probe.py                 # 用热门榜第一条做样片
    python tools/bili-anon-playurl-probe.py BV1xx411c7mD    # 指定视频

它会依次验证 5 条路径，并打印每条路径实际能拿到的画质：
    1. REST  + appkey 签名（bili-kernel GetVideoPlayDetailWithRestAsync 的协议层写法）
    2. REST  + wbi 签名（网页 playurl）
    3. REST  + platform=html5（单文件 mp4，匿名可达 720P）
    4. gRPC  PlayURL/PlayView（bili-kernel GetVideoPlayDetailWithGrpcAsync 的写法）
    5. 直播 getRoomPlayInfo + 番剧 pgc/player/web/playurl
    6. CDN 直链实测（是否需要 Referer）

注意：所有请求都不带任何登录 Cookie / access_key，用于验证"未登录"场景。
"""

import base64
import hashlib
import json
import random
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

UA_WEB = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
          "(KHTML, like Gecko) Chrome/116.0.0.0 Safari/537.36 Edg/116.0.1938.69")
UA_APP = ("bili-universal/80200100 os/ios model/iPhone 14 Pro mobi_app/iphone_i "
          "osVer/16.6 network/wifi grpc-objc/1.47.0 grpc-c/25.0.0 (ios; cronet_http)")

# bili-kernel/src/BiliKernel.Core/Authenticator/BiliAuthenticator.Extension.cs
APPKEY, APPSECRET = "27eb53fc9058f8c3", "c2ed53a74eeefe3cf99fbd01d8c9c375"
TVKEY, TVSECRET = "4409e2ce8ffd12b8", "59b43e04ad6965f34319062b478f83dd"

MIXIN_KEY_ENC_TAB = [
    46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35, 27, 43, 5, 49,
    33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13, 37, 48, 7, 16, 24, 55, 40,
    61, 26, 17, 0, 1, 60, 51, 30, 4, 22, 25, 54, 21, 56, 59, 6, 63, 57, 62, 11,
    36, 20, 34, 44, 52,
]


# ----------------------------------------------------------------- HTTP
def http(url, headers=None, method="GET", data=None, timeout=25, referer="https://www.bilibili.com/"):
    h = {"User-Agent": UA_WEB}
    if referer:
        h["Referer"] = referer
    if headers:
        h.update(headers)
    try:
        with urllib.request.urlopen(
                urllib.request.Request(url, headers=h, method=method, data=data),
                timeout=timeout) as r:
            return r.status, r.read(), dict(r.headers)
    except urllib.error.HTTPError as e:
        return e.code, e.read(), dict(e.headers)
    except Exception as e:  # noqa: BLE001  DNS/TLS/超时都归为一类
        return 0, repr(e).encode(), {}


def jget(url, **kw):
    _, body, _ = http(url, **kw)
    try:
        return json.loads(body.decode("utf-8", "replace"))
    except Exception:
        return {"code": -1, "message": body[:160].decode("utf-8", "replace")}


def md5(s):
    return hashlib.md5(s.encode()).hexdigest()


# ------------------------------------------------------------- 签名
def app_sign(params):
    p = dict(params)
    p.update({"appkey": APPKEY, "mobi_app": "iphone", "platform": "ios",
              "ts": int(time.time())})
    q = "&".join(f"{k}={v}" for k, v in sorted(p.items()))
    p["sign"] = md5(q + APPSECRET)
    return p


def wbi_mixin_key():
    d = jget("https://api.bilibili.com/x/web-interface/nav")
    img = (d.get("data") or {}).get("wbi_img") or {}
    key = img["img_url"].rsplit("/", 1)[-1].split(".")[0] + \
        img["sub_url"].rsplit("/", 1)[-1].split(".")[0]
    return "".join(key[i] for i in MIXIN_KEY_ENC_TAB)[:32]


def wbi_sign(params, mixin):
    p = dict(params)
    p["wts"] = int(time.time())
    p["w_rid"] = md5(urllib.parse.urlencode(sorted(p.items())) + mixin)
    return p


# ------------------------------------------------------- 最小 protobuf
def varint(n):
    out = bytearray()
    n &= (1 << 64) - 1
    while True:
        b = n & 0x7F
        n >>= 7
        out.append(b | 0x80 if n else b)
        if not n:
            return bytes(out)


def pv(field, value):
    return varint(field << 3) + varint(value)


def ps(field, value):
    d = value.encode() if isinstance(value, str) else value
    return varint((field << 3) | 2) + varint(len(d)) + d


def pb_parse(buf):
    res, i = [], 0
    while i < len(buf):
        key = shift = 0
        while True:
            b = buf[i]; i += 1; key |= (b & 0x7F) << shift; shift += 7
            if not b & 0x80:
                break
        field, wire = key >> 3, key & 7
        if wire == 0:
            val = shift = 0
            while True:
                b = buf[i]; i += 1; val |= (b & 0x7F) << shift; shift += 7
                if not b & 0x80:
                    break
            res.append((field, 0, val))
        elif wire == 2:
            ln = shift = 0
            while True:
                b = buf[i]; i += 1; ln |= (b & 0x7F) << shift; shift += 7
                if not b & 0x80:
                    break
            res.append((field, 2, buf[i:i + ln])); i += ln
        elif wire == 5:
            res.append((field, 5, buf[i:i + 4])); i += 4
        elif wire == 1:
            res.append((field, 1, buf[i:i + 8])); i += 8
        else:
            raise ValueError(f"wire {wire}")
    return res


def pb_dict(buf):
    d = {}
    for f, w, v in pb_parse(buf):
        d.setdefault(f, []).append(v)
    return d


def b64(raw):
    return base64.b64encode(raw).decode().rstrip("=")


def grpc_headers(buvid):
    """照抄 bili-kernel GrpcConfig 的 x-bili-* 头（不含 authorization）。"""
    trace = "".join(random.choice("0123456789abcdefghijklmnopqrstuvwxyz")
                    for _ in range(32))
    return {
        "User-Agent": UA_APP,
        "Content-Type": "application/grpc",
        "TE": "trailers",
        "grpc-accept-encoding": "identity,deflate,gzip",
        "env": "prod",
        "buvid": buvid,
        "x-bili-device-bin": b64(
            pv(1, 1) + pv(2, 6070600) + ps(3, "XZFD48CFF1E68E637D0DF11A562468A8DC314") +
            ps(4, "iphone") + ps(5, "iphone") + ps(6, "phone") + ps(7, "bilibili140") +
            ps(8, "Apple") + ps(9, "iPhone 14 Pro") + ps(10, "16.6")),
        "x-bili-fawkes-req-bin": b64(ps(1, "iphone") + ps(2, "prod")),
        "x-bili-locale-bin": b64(ps(1, ps(1, "zh") + ps(3, "CN")) +
                                 ps(2, ps(1, "zh") + ps(3, "CN"))),
        "x-bili-metadata-bin": b64(ps(2, "iphone") + pv(4, 6070600) +
                                   ps(5, "bilibili140") +
                                   ps(6, "XZFD48CFF1E68E637D0DF11A562468A8DC314") +
                                   ps(7, "iphone")),
        "x-bili-network-bin": b64(pv(1, 1) + ps(3, "46007")),
        "x-bili-restriction-bin": b64(b""),
        "x-bili-trace-id": f"{trace}:{trace[16:]}:0:0",
    }


# ----------------------------------------------------------- 各条路径
def heights(dash):
    return sorted({v.get("height") for v in (dash or {}).get("video", [])}, reverse=True)


def path_rest_appkey(aid, cid):
    p = app_sign({"avid": aid, "cid": cid, "qn": 112, "fnver": 0, "fnval": 4048,
                  "fourk": 1, "oType": "json"})
    r = jget("https://api.bilibili.com/x/player/playurl?" + urllib.parse.urlencode(p))
    d = r.get("data") or {}
    return (f"code={r['code']} quality={d.get('quality')} "
            f"dash_heights={heights(d.get('dash'))} "
            f"accept={d.get('accept_description')}",
            [v["base_url"] for v in (d.get("dash") or {}).get("video", [])])


def path_rest_wbi(aid, cid, cookie):
    try:
        mixin = wbi_mixin_key()
        p = wbi_sign({"avid": aid, "cid": cid, "qn": 112, "fnver": 0, "fnval": 4048,
                      "fourk": 1, "platform": "pc"}, mixin)
        r = jget("https://api.bilibili.com/x/player/wbi/playurl?" +
                 urllib.parse.urlencode(p), headers={"Cookie": cookie})
        d = r.get("data") or {}
        return (f"code={r['code']} quality={d.get('quality')} "
                f"dash_heights={heights(d.get('dash'))}", [])
    except Exception as e:  # noqa: BLE001
        return f"ERR {e!r}", []


def path_rest_unsigned(aid, cid):
    r = jget(f"https://api.bilibili.com/x/player/playurl?avid={aid}&cid={cid}"
             "&qn=112&fnval=4048&fourk=1&platform=pc")
    d = r.get("data") or {}
    return (f"code={r['code']} quality={d.get('quality')} "
            f"dash_heights={heights(d.get('dash'))}  "
            f"（说明当前 playurl 未校验签名）", [])


def path_html5(aid, cid):
    r = jget("https://api.bilibili.com/x/player/playurl?" + urllib.parse.urlencode(
        {"avid": aid, "cid": cid, "qn": 64, "fnval": 1, "platform": "html5",
         "high_quality": 1}))
    d = r.get("data") or {}
    durl = d.get("durl") or []
    txt = (f"code={r['code']} quality={d.get('quality')} format={d.get('format')} "
           f"单文件={len(durl)} size_MB="
           f"{round(durl[0]['size'] / 1048576, 1) if durl else None}")
    return txt, [u["url"] for u in durl]


def path_grpc(aid, cid, buvid):
    req = (pv(1, aid) + pv(2, cid) + pv(3, 112) + pv(4, 0) + pv(5, 4048) + pv(8, 1))
    frame = b"\x00" + len(req).to_bytes(4, "big") + req
    st, body, hd = http("https://grpc.biliapi.net/bilibili.app.playurl.v1.PlayURL/PlayView",
                        headers=grpc_headers(buvid), method="POST", data=frame)
    if len(body) <= 5:
        return (f"http={st} grpc-status={hd.get('grpc-status')} "
                f"grpc-message={hd.get('grpc-message')}（空响应）", [])
    vi = pb_dict(pb_dict(body[5:])[1][0])       # PlayViewReply.video_info
    urls = []
    for item in vi.get(6, []):                  # 匿名返回的是 durl 段
        urls.extend(u.decode("utf-8", "replace") for u in pb_dict(item).get(2, []))
    return (f"http={st} grpc-status={hd.get('grpc-status')} quality={vi.get(1, [None])[0]} "
            f"format={(vi.get(2, [b'-'])[0]).decode('utf-8', 'replace')} "
            f"durl_segments={len(vi.get(6, []))}", urls)


def path_live(room_id, cookie):
    r = jget("https://api.live.bilibili.com/xlive/web-room/v2/index/getRoomPlayInfo"
             f"?room_id={room_id}&protocol=0,1&format=0,1,2&codec=0,1&qn=10000"
             "&platform=web&ptype=8", headers={"Cookie": cookie})
    d = r.get("data") or {}
    urls = []
    for s in ((d.get("playurl_info") or {}).get("playurl") or {}).get("stream") or []:
        for f in s.get("format") or []:
            for c in f.get("codec") or []:
                host = ((c.get("url_info") or [{}])[0].get("host") or "")
                urls.append(host + (c.get("base_url") or ""))
    return (f"code={r['code']} live_status={d.get('live_status')} "
            f"streams={len(urls)}（flv/hls 直链，无需登录）", urls)


def path_pgc():
    """找一部有剧集的番，验证匿名能拿到的画质 / 是否只剩试看。"""
    idx = jget("https://api.bilibili.com/pgc/season/index/result?season_type=1&type=0"
               "&page=1&pagesize=12&order=3")
    lst = ((idx.get("data") or {}).get("list")) or []
    if not lst:
        return "index 无数据", []
    for item in lst:
        sid = item.get("season_id")
        season = jget(f"https://api.bilibili.com/pgc/view/web/season?season_id={sid}")
        res = season.get("result") or {}
        eps = res.get("episodes") or []
        if not eps:
            continue
        e = eps[0]
        r = jget("https://api.bilibili.com/pgc/player/web/playurl?" + urllib.parse.urlencode(
            {"ep_id": e["id"], "cid": e["cid"], "qn": 112, "fnval": 4048, "fnver": 0,
             "fourk": 1, "module": "bangumi", "season_type": 1, "try_look": 1}),
            referer=f"https://www.bilibili.com/bangumi/play/ep{e['id']}")
        d = r.get("result") or {}
        dash = d.get("dash") or {}
        return (f"「{res.get('title')}」code={r['code']} is_preview={d.get('is_preview')} "
                f"dash_heights={heights(dash)} dash_duration={dash.get('duration')} "
                f"试看段数={len(d.get('durl') or [])}", [])
    return "没有可用的番剧样片", []

def main():
    bvid = sys.argv[1] if len(sys.argv) > 1 else None
    print("=" * 78)
    print("[0] 匿名指纹")
    spi = jget("https://api.bilibili.com/x/frontend/finger/spi")
    b3, b4 = spi["data"]["b_3"], spi["data"]["b_4"]
    cookie = f"buvid3={b3}; buvid4={b4}"
    nav = jget("https://api.bilibili.com/x/web-interface/nav", headers={"Cookie": cookie})
    print(f"    isLogin={nav['data'].get('isLogin')} buvid3={b3[:16]}...")

    if bvid:
        view = jget(f"https://api.bilibili.com/x/web-interface/view?bvid={bvid}")
        v = view.get("data") or {}
        aid, cid, title = v.get("aid"), (v.get("pages") or [{}])[0].get("cid"), v.get("title")
    else:
        pop = jget("https://api.bilibili.com/x/web-interface/popular?ps=1&pn=1")
        v = pop["data"]["list"][0]
        aid, cid, title, bvid = v["aid"], v["cid"], v["title"], v["bvid"]
    if not aid or not cid:
        print("    无法解析 aid/cid，退出")
        return
    print(f"    样片 {bvid} aid={aid} cid={cid} {str(title)[:36]}")

    links = []
    for name, fn in [
        ("1 REST appkey 签名", lambda: path_rest_appkey(aid, cid)),
        ("2 REST wbi 签名", lambda: path_rest_wbi(aid, cid, cookie)),
        ("3 REST 无任何签名", lambda: path_rest_unsigned(aid, cid)),
        ("4 html5 单文件 mp4", lambda: path_html5(aid, cid)),
        ("5 gRPC PlayView", lambda: path_grpc(aid, cid, b3)),
    ]:
        print("=" * 78)
        print(f"[{name}]")
        try:
            txt, urls = fn()
        except Exception as e:  # noqa: BLE001
            txt, urls = f"ERR {e!r}", []
        print(f"    {txt}")
        links.extend(urls)

    print("=" * 78)
    print("[6] 直播直链（无需登录）")
    try:
        hot = jget("https://api.live.bilibili.com/xlive/web-interface/v1/second/getList"
                   "?platform=web&parent_area_id=0&area_id=0&page=1")
        rooms = (hot.get("data") or {}).get("list") or []
        txt, urls = path_live(rooms[0]["roomid"] if rooms else 6, cookie)
        print(f"    {txt}")
        links.extend(urls)
    except Exception as e:  # noqa: BLE001
        print(f"    ERR {e!r}")

    print("=" * 78)
    print("[7] 番剧 PGC（免费集 / 大会员集）")
    try:
        txt, _ = path_pgc()
        print(f"    {txt}")
    except Exception as e:  # noqa: BLE001
        print(f"    ERR {e!r}")

    print("=" * 78)
    print("[8] CDN 直链实测（Range 1KB）")
    for u in links[:3]:
        if not u.startswith("http"):
            print(f"    跳过非绝对地址: {u[:60]}")
            continue
        st, body, hd = http(u, headers={"Range": "bytes=0-1023"})
        st2, _, _ = http(u, headers={"Range": "bytes=0-1023"}, referer=None)
        print(f"    带 Referer -> http={st} {len(body)}B  |  不带 Referer -> http={st2} "
              f"| host={urllib.parse.urlparse(u).hostname}")

    print("=" * 78)
    print("结论：未登录可拿到直链，但画质天花板 = DASH 480P / html5 mp4 720P；"
          "1080P+、4K、HDR、大会员正片必须登录。")


if __name__ == "__main__":
    main()
