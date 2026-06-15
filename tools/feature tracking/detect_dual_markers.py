#!/usr/bin/env python3
"""
Barrel axis from two ArUco markers — webcam tracker.

Place one ArUco marker (DICT_4X4_50) near the plunger end and another near the
needle end of the syringe, each on its own small FLAT mount (tab/clip) — not
wrapped around the cylindrical body. Each marker gets a 6-DoF pose via
solvePnP (SOLVEPNP_IPPE_SQUARE); the barrel axis is the 3D vector between the
two marker positions (needle_pos - plunger_pos), drawn as an arrow and reported
in metres.

Curved surfaces: ArUco detection assumes a PLANAR marker. A marker printed flat
and wrapped around a cylinder warps non-projectively — corner detection becomes
unreliable and the recovered pose is wrong. Mount each marker on a small flat
tab/clip instead of the curved barrel itself; the 2-marker axis approach then
sidesteps curvature entirely (only the marker positions matter, not the barrel
surface between them).

Keys:  q quit   s save annotated screenshot
"""

import argparse
import time

import cv2
import numpy as np


# ----------------------------------------------------------------------------------------
# Camera model
# ----------------------------------------------------------------------------------------
def default_camera_matrix(w, h):
    """fx=fy=max(w,h), principal point at image centre — standard rough default for
    webcams without a calibration file (see cv2.aruco pose-estimation examples)."""
    f = float(max(w, h))
    return np.array([[f, 0.0, w / 2.0], [0.0, f, h / 2.0], [0.0, 0.0, 1.0]], np.float64)


def load_calibration(path, w, h):
    if not path:
        return default_camera_matrix(w, h), np.zeros((5, 1), np.float64)
    data = np.load(path)
    return data["camera_matrix"].astype(np.float64), data["dist_coeffs"].astype(np.float64)


# ----------------------------------------------------------------------------------------
# ArUco fiducial
# ----------------------------------------------------------------------------------------
class ArucoTracker:
    def __init__(self, marker_length_m):
        self.dictionary = cv2.aruco.getPredefinedDictionary(cv2.aruco.DICT_4X4_50)
        self.detector = cv2.aruco.ArucoDetector(self.dictionary, cv2.aruco.DetectorParameters())
        self.marker_length = marker_length_m
        s = marker_length_m / 2.0
        # Object points in marker frame, matching ArUco corner order (TL, TR, BR, BL).
        self.obj_pts = np.array([[-s, s, 0], [s, s, 0], [s, -s, 0], [-s, -s, 0]], np.float64)

    def detect(self, frame, camera_matrix, dist_coeffs):
        corners, ids, _ = self.detector.detectMarkers(frame)
        results = []
        if ids is None:
            return results
        for c, marker_id in zip(corners, ids.flatten()):
            img_pts = c.reshape(4, 2).astype(np.float64)
            ok, rvec, tvec = cv2.solvePnP(
                self.obj_pts, img_pts, camera_matrix, dist_coeffs, flags=cv2.SOLVEPNP_IPPE_SQUARE
            )
            results.append({"id": int(marker_id), "corners": img_pts, "rvec": rvec, "tvec": tvec, "ok": ok})
        return results


# ----------------------------------------------------------------------------------------
# Two-marker barrel axis
# ----------------------------------------------------------------------------------------
PINK = (220, 60, 255)
ORANGE = (60, 170, 255)
GREEN = (80, 255, 80)


def two_marker_axis(aruco_results, plunger_id, needle_id, camera_matrix, dist_coeffs):
    """Barrel axis from two ArUco markers' 3D positions: ID=plunger_id near the
    plunger end, ID=needle_id near the needle end. axis = needle_pos - plunger_pos
    in camera-frame metres."""
    by_id = {r["id"]: r for r in aruco_results if r["ok"]}
    if plunger_id not in by_id or needle_id not in by_id:
        return None
    p_tvec = by_id[plunger_id]["tvec"].flatten()
    n_tvec = by_id[needle_id]["tvec"].flatten()
    vec = n_tvec - p_tvec
    length = float(np.linalg.norm(vec))
    direction = vec / length if length > 1e-9 else vec
    pts3d = np.array([p_tvec, n_tvec], np.float64).reshape(-1, 1, 3)
    pts2d, _ = cv2.projectPoints(pts3d, np.zeros(3), np.zeros(3), camera_matrix, dist_coeffs)
    pts2d = pts2d.reshape(-1, 2)
    return {"p2d": pts2d[0], "n2d": pts2d[1], "vec": vec, "dir": direction, "length": length}


def draw_two_marker_axis(frame, axis_info):
    if axis_info is None:
        return
    p, n = np.round(axis_info["p2d"]).astype(int), np.round(axis_info["n2d"]).astype(int)
    cv2.arrowedLine(frame, tuple(p), tuple(n), PINK, 3, cv2.LINE_AA, tipLength=0.12)


def draw_aruco(frame, results, camera_matrix, dist_coeffs, marker_length):
    if results:
        cv2.aruco.drawDetectedMarkers(frame, [r["corners"].reshape(1, 4, 2).astype(np.float32) for r in results],
                                       np.array([[r["id"]] for r in results], np.int32))
        for r in results:
            if r["ok"]:
                cv2.drawFrameAxes(frame, camera_matrix, dist_coeffs, r["rvec"], r["tvec"], marker_length * 0.75, 2)


def _status_color(index, text, last_index):
    if index == 0:
        return GREEN
    if text.startswith("AXIS"):
        return PINK
    if index == last_index:
        return ORANGE
    return (235, 235, 235)


def draw_status(frame, aruco_results, axis_info, fps):
    lines = [f"ARUCO: {len(aruco_results)} marker(s)"]
    for r in aruco_results:
        if r["ok"]:
            tv = r["tvec"].flatten()
            lines.append(f"  id {r['id']}: t=({tv[0]:+.3f},{tv[1]:+.3f},{tv[2]:+.3f})m")
    if axis_info is not None:
        d = axis_info["dir"]
        lines.append(f"AXIS: len={axis_info['length'] * 1000:0.1f}mm  dir=({d[0]:+.2f},{d[1]:+.2f},{d[2]:+.2f})")
    lines.append(f"fps: {fps:0.0f}    [q]uit [s]ave")

    y = 24
    for i, t in enumerate(lines):
        col = _status_color(i, t, len(lines) - 1)
        cv2.putText(frame, t, (10, y), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (0, 0, 0), 3, cv2.LINE_AA)
        cv2.putText(frame, t, (10, y), cv2.FONT_HERSHEY_SIMPLEX, 0.55, col, 1, cv2.LINE_AA)
        y += 22


def open_source(args):
    if args.source is not None:
        still_image = cv2.imread(args.source)
        if still_image is not None:
            return still_image, None
        cap = cv2.VideoCapture(args.source)
    else:
        cap = cv2.VideoCapture(args.camera)
        cap.set(cv2.CAP_PROP_FRAME_WIDTH, args.width)
        cap.set(cv2.CAP_PROP_FRAME_HEIGHT, args.height)
    if not cap.isOpened():
        raise SystemExit(f"Could not open source (camera={args.camera}, source={args.source}).")
    return None, cap


def frame_size(still_image, cap, args):
    if still_image is not None:
        return still_image.shape[:2]
    return (int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT)) or args.height,
            int(cap.get(cv2.CAP_PROP_FRAME_WIDTH)) or args.width)


def next_frame(still_image, cap, args):
    """Next frame, or None when the stream is exhausted (video files loop instead)."""
    if still_image is not None:
        return still_image.copy()
    while True:
        ok, frame = cap.read()
        if ok:
            return frame
        if args.source is None:
            return None
        cap.set(cv2.CAP_PROP_POS_FRAMES, 0)


# ----------------------------------------------------------------------------------------
def main():
    ap = argparse.ArgumentParser(description="2-ArUco-marker barrel axis webcam tracker")
    ap.add_argument("--camera", type=int, default=0, help="webcam index (default 0)")
    ap.add_argument("--source", type=str, default=None, help="video/image path instead of the webcam")
    ap.add_argument("--width", type=int, default=1280, help="requested capture width")
    ap.add_argument("--height", type=int, default=720, help="requested capture height")
    ap.add_argument("--calib", type=str, default=None,
                    help="camera calibration .npz with camera_matrix / dist_coeffs (default: rough estimate)")
    ap.add_argument("--aruco-length", type=float, default=0.024, help="ArUco marker black-square side, metres")
    ap.add_argument("--plunger-id", type=int, default=0, help="ArUco ID mounted near the plunger end")
    ap.add_argument("--needle-id", type=int, default=1, help="ArUco ID mounted near the needle end")
    args = ap.parse_args()

    aruco_tracker = ArucoTracker(args.aruco_length)
    still_image, cap = open_source(args)
    h, w = frame_size(still_image, cap, args)
    camera_matrix, dist_coeffs = load_calibration(args.calib, w, h)

    win = "2-ArUco barrel axis tracker"
    cv2.namedWindow(win, cv2.WINDOW_NORMAL)
    last_t, fps, shot = time.monotonic(), 0.0, 0

    while True:
        frame = next_frame(still_image, cap, args)
        if frame is None:
            break

        aruco_results = aruco_tracker.detect(frame, camera_matrix, dist_coeffs)
        axis_info = two_marker_axis(aruco_results, args.plunger_id, args.needle_id, camera_matrix, dist_coeffs)

        now = time.monotonic()
        dt = now - last_t
        last_t = now
        if dt > 0:
            fps = 0.9 * fps + 0.1 * (1.0 / dt)

        draw_aruco(frame, aruco_results, camera_matrix, dist_coeffs, args.aruco_length)
        draw_two_marker_axis(frame, axis_info)
        draw_status(frame, aruco_results, axis_info, fps)

        cv2.imshow(win, frame)

        key = cv2.waitKey(1) & 0xFF
        if key in (ord("q"), 27):
            break
        if key == ord("s"):
            fn = f"aruco_axis_shot_{shot:03d}.png"
            cv2.imwrite(fn, frame)
            print(f"saved {fn}")
            shot += 1

    if cap is not None:
        cap.release()
    cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
