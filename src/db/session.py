from sqlmodel import SQLModel, create_engine, Session
from src.config import get_settings

settings = get_settings()
engine = create_engine(settings.database_url, echo=settings.debug)

def get_session():
    with Session(engine) as session:
        yield session

# Usage example:
# db = Database()
# with db.get_session() as session:
#     ... # use session
