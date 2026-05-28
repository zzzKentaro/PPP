#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
PPP prototype sender
- Captures a webcam feed
- Lets you click the 4 corners of the projection area once
- Warps the wall into projection space
- Detects pink sticky notes
- Sends obstacle rectangles to Unity over UDP as JSON

Tested design:
Python/OpenCV -> UDP(JSON) -> Unity 2D

Keys:
  q : quit
  c : recalibrate 4 corners
  s : save calibration
  l : reload calibration file
"""

from __future__ import annotations

import argparse
import json
import socket
import time
from dataclasses import dataclass, asdict
from pathlib import Path
from typing import List

import cv2
import numpy as np

DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 5005
DEFAULT_CAMERA_INDEX = 0
PROJECTION_W = 1280
PROJECTION_H = 720

LOWER_HSV = np.array([140, 60, 60], dtype=np.uint8)
UPPER_HSV = np.array([179, 255, 255], dtype=np.uint8)

MIN_CONTOUR_AREA = 1000
MORPH_KERNEL = 5
SEND_INTERVAL_SEC = 1.0 / 30.0
CALIBRATION_FILE = "ppp_calibration.json"


@dataclass
class Obstacle:
    id: int
    x: float
    y: float
    w: float
    h: float
    angle: float


class CornerPicker:
    def __init__(self, window_name: str):
        self.window_name = window_name
        self.points: List[tuple[int, int]] = []

    def mouse_callback(self, event, x, y, flags, param):
        if event == cv2.EVENT_LBUTTONDOWN and len(self.points) < 4:
            self.points.append((x, y))
            print(f"[calib] point {len(self.points)} = {(x, y)}")

    def reset(self):
        self.points = []

    def draw(self, frame: np.ndarray) -> np.ndarray:
        vis = frame.copy()
        for i, p in enumerate(self.points):
            cv2.circle(vis, p, 6, (0, 255, 0), -1)
            cv2.putText(vis, str(i + 1), (p[0] + 8, p[1] - 8),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2, cv2.LINE_AA)
        if len(self.points) == 4:
            pts = np.array(self.points, dtype=np.int32).reshape(-1, 1, 2)
            cv2.polylines(vis, [pts], True, (255, 0, 0), 2)
        cv2.putText(vis,
                    "Click 4 corners: top-left -> top-right -> bottom-right -> bottom-left",
                    (16, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (50, 200, 255), 2, cv2.LINE_AA)
        cv2.putText(vis, "Keys: q quit / c recalibrate / s save / l load", (16, 60),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.6, (50, 200, 255), 2, cv2.LINE_AA)
        return vis


def build_homography(clicked_points: List[tuple[int, int]], out_w: int, out_h: int) -> np.ndarray:
    src = np.array(clicked_points, dtype=np.float32)
    dst = np.array([[0, 0], [out_w - 1, 0], [out_w - 1, out_h - 1], [0, out_h - 1]], dtype=np.float32)
    return cv2.getPerspectiveTransform(src, dst)


def save_calibration(path: Path, points: List[tuple[int, int]], out_w: int, out_h: int) -> None:
    payload = {"points": points, "projection_w": out_w, "projection_h": out_h}
    path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"[calib] saved to {path}")


def load_calibration(path: Path) -> List[tuple[int, int]] | None:
    if not path.exists():
        return None
    payload = json.loads(path.read_text(encoding="utf-8"))
    points = payload.get("points")
    if not points or len(points) != 4:
        return None
    return [tuple(map(int, p)) for p in points]


def normalize_rect(center_x: float, center_y: float, w: float, h: float, image_w: int, image_h: int):
    return center_x / image_w, center_y / image_h, w / image_w, h / image_h


def detect_sticky_notes(warped_bgr: np.ndarray):
    hsv = cv2.cvtColor(warped_bgr, cv2.COLOR_BGR2HSV)
    mask = cv2.inRange(hsv, LOWER_HSV, UPPER_HSV)

    kernel = np.ones((MORPH_KERNEL, MORPH_KERNEL), np.uint8)
    mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, kernel)
    mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, kernel)

    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    debug = warped_bgr.copy()
    obstacles: List[Obstacle] = []

    current_id = 0
    for cnt in contours:
        area = cv2.contourArea(cnt)
        if area < MIN_CONTOUR_AREA:
            continue

        rect = cv2.minAreaRect(cnt)
        (cx, cy), (w, h), angle = rect

        if w < h:
            w, h = h, w
            angle += 90.0

        nx, ny, nw, nh = normalize_rect(cx, cy, w, h, warped_bgr.shape[1], warped_bgr.shape[0])
        obstacle = Obstacle(id=current_id, x=float(nx), y=float(ny), w=float(nw), h=float(nh), angle=float(angle))
        obstacles.append(obstacle)
        current_id += 1

        box = cv2.boxPoints(((cx, cy), (w, h), angle))
        box = np.int32(box)
        cv2.drawContours(debug, [box], 0, (0, 255, 0), 2)
        cv2.circle(debug, (int(cx), int(cy)), 4, (0, 0, 255), -1)
        cv2.putText(debug, f"id={obstacle.id} a={angle:.1f}", (int(cx) + 8, int(cy) - 8),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.55, (0, 255, 0), 2, cv2.LINE_AA)

    return obstacles, mask, debug


def maybe_destroy_window(name: str) -> None:
    try:
        if cv2.getWindowProperty(name, cv2.WND_PROP_VISIBLE) >= 0:
            cv2.destroyWindow(name)
    except cv2.error:
        pass


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default=DEFAULT_HOST)
    parser.add_argument("--port", type=int, default=DEFAULT_PORT)
    parser.add_argument("--camera", type=int, default=DEFAULT_CAMERA_INDEX)
    parser.add_argument("--width", type=int, default=PROJECTION_W)
    parser.add_argument("--height", type=int, default=PROJECTION_H)
    parser.add_argument("--calib", type=str, default=CALIBRATION_FILE)
    parser.add_argument("--no-save", action="store_true")
    args = parser.parse_args()

    out_w = args.width
    out_h = args.height
    calib_path = Path(args.calib)

    cap = cv2.VideoCapture(args.camera)
    if not cap.isOpened():
        raise RuntimeError(f"Camera {args.camera} could not be opened.")

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    picker = CornerPicker("PPP Camera")
    cv2.namedWindow("PPP Camera")
    cv2.setMouseCallback("PPP Camera", picker.mouse_callback)

    loaded = load_calibration(calib_path)
    if loaded:
        picker.points = loaded
        print(f"[calib] loaded {calib_path}")

    last_send = 0.0

    while True:
        ok, frame = cap.read()
        if not ok:
            print("[warn] camera read failed")
            time.sleep(0.05)
            continue

        if len(picker.points) == 4:
            H = build_homography(picker.points, out_w, out_h)
            warped = cv2.warpPerspective(frame, H, (out_w, out_h))
            obstacles, mask, debug_warped = detect_sticky_notes(warped)

            now = time.time()
            if now - last_send >= SEND_INTERVAL_SEC:
                payload = {"frame_w": out_w, "frame_h": out_h, "obstacles": [asdict(o) for o in obstacles]}
                message = json.dumps(payload, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
                sock.sendto(message, (args.host, args.port))
                last_send = now

            cv2.putText(debug_warped, f"obstacles={len(obstacles)} send={args.host}:{args.port}", (12, 28),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2, cv2.LINE_AA)
            cv2.imshow("PPP Warped", debug_warped)
            cv2.imshow("PPP Mask", mask)
        else:
            maybe_destroy_window("PPP Warped")
            maybe_destroy_window("PPP Mask")

        cv2.imshow("PPP Camera", picker.draw(frame))

        key = cv2.waitKey(1) & 0xFF
        if key == ord("q"):
            break
        if key == ord("c"):
            print("[calib] reset")
            picker.reset()
        if key == ord("s") and len(picker.points) == 4:
            save_calibration(calib_path, picker.points, out_w, out_h)
        if key == ord("l"):
            loaded = load_calibration(calib_path)
            if loaded:
                picker.points = loaded
                print(f"[calib] reloaded {calib_path}")
            else:
                print(f"[calib] no calibration file at {calib_path}")

        if len(picker.points) == 4 and (not args.no_save) and (not calib_path.exists()):
            save_calibration(calib_path, picker.points, out_w, out_h)

    cap.release()
    sock.close()
    cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
