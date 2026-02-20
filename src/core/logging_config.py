import logging
import traceback
import sys

PROJECT_ROOT = "SpendSense-backend\\src"

class CleanTracebackFormatter(logging.Formatter):
    def formatException(self, exc_info):
        exc_type, exc_value, exc_tb = exc_info
        filtered_frames = []

        for frame in traceback.extract_tb(exc_tb):
            if PROJECT_ROOT in frame.filename:
                filtered_frames.append(frame)

        if not filtered_frames:
            # Fallback: just show the last frame
            filtered_frames = traceback.extract_tb(exc_tb)[-1:]

        tb_str = "Traceback (most recent call last):\n"
        tb_str += "".join(traceback.format_list(filtered_frames))
        tb_str += "".join(traceback.format_exception_only(exc_type, exc_value))
        return tb_str.strip()

def setup_logging():
    formatter = CleanTracebackFormatter(
        fmt="[%(asctime)s] %(levelname)s in %(module)s: %(message)s"
    )

    handler = logging.StreamHandler(sys.stdout)
    handler.setFormatter(formatter)

    root_logger = logging.getLogger()
    root_logger.setLevel(logging.INFO)
    root_logger.handlers = [handler]
