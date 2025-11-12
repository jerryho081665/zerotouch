import asyncio
import websockets
import json
import time
import random

GESTURES = [
    "swipe_left",
    "swipe_right",
    "push",
    "tap",
    "rotate_clockwise",
    "rotate_counterclockwise"
]

async def handle_client(ws):
    print("Client connected.")

    try:
        while True:
            gesture = random.choice(GESTURES)
            confidence = round(random.uniform(0.8, 1.0), 2)

            data = {
                "ts": round(time.time(), 3),
                "gesture": gesture,
                "confidence": confidence
            }

            await ws.send(json.dumps(data))
            print(f"Sent: {data}")

            # simulate variable delay between messages
            await asyncio.sleep(random.uniform(0.5, 2.0))

    except websockets.exceptions.ConnectionClosedOK:
        print("Client disconnected normally.")
    except websockets.exceptions.ConnectionClosedError as e:
        print(f"Connection closed unexpectedly: {e}")
    except Exception as e:
        print(f"Unexpected error: {e}")

async def main():
    # reuse_port=True

    async with websockets.serve(handle_client, "localhost", 8765):
        print("Mock server started on ws://localhost:8765")
        await asyncio.Future()  # keep running till interrupted

if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("Server stopped manually.")
