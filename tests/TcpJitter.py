import argparse
import asyncio
import itertools
from pathlib import Path


async def forward(reader, writer):
    delays = itertools.cycle((0.004, 0.013, 0.075, 0.008, 0.120, 0.005, 0.020, 0.010))
    while True:
        header = await reader.readexactly(4)
        size = int.from_bytes(header, "big")
        if not 0 < size <= 256 * 1024:
            return
        data = await reader.readexactly(size)
        await asyncio.sleep(next(delays))
        writer.write(header[:2])
        await writer.drain()
        await asyncio.sleep(0.002)
        writer.write(header[2:] + data)
        await writer.drain()


async def relay(reader, writer, host, port):
    try:
        upstream, target = await asyncio.open_connection(host, port)
    except OSError:
        writer.close()
        await writer.wait_closed()
        return
    tasks = [asyncio.create_task(forward(reader, target)), asyncio.create_task(forward(upstream, writer))]
    try:
        await asyncio.wait(tasks, return_when=asyncio.FIRST_COMPLETED)
    finally:
        for task in tasks:
            task.cancel()
        await asyncio.gather(*tasks, return_exceptions=True)
        writer.close()
        target.close()
        await asyncio.gather(writer.wait_closed(), target.wait_closed(), return_exceptions=True)


async def main(args):
    server = await asyncio.start_server(
        lambda reader, writer: relay(reader, writer, args.target, args.host_port), args.listen, args.port
    )
    async with server:
        args.ready.touch()
        while not args.stop.exists():
            await asyncio.sleep(0.1)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Delay and fragment LAN data in both directions across TCP.")
    parser.add_argument("listen")
    parser.add_argument("port", type=int)
    parser.add_argument("target")
    parser.add_argument("host_port", type=int)
    parser.add_argument("ready", type=Path)
    parser.add_argument("stop", type=Path)
    asyncio.run(main(parser.parse_args()))
